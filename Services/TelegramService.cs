using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using MultiWave.Models;
using WTelegram;
using TL;
using System.Threading.Tasks;

namespace MultiWave.Services;

public class TelegramService
{
    private readonly AppConfigService _config;
    private readonly string _sessionsDir;
    private readonly Dictionary<string, TelegramAccount> _pending = new(StringComparer.OrdinalIgnoreCase);
    private int? _apiId;
    private string? _apiHash;
    private string? _currentPhone;

    public ObservableCollection<TelegramAccount> Accounts { get; } = new();
    public int? ApiId => _apiId;
    public string? ApiHash => _apiHash;

    public TelegramService(AppConfigService config)
    {
        _config = config;
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _sessionsDir = Path.Combine(roaming, "MultiWave", "sessions");
        Directory.CreateDirectory(_sessionsDir);

        var envId = Environment.GetEnvironmentVariable("TG_API_ID") ?? Environment.GetEnvironmentVariable("TELEGRAM_API_ID");
        if (int.TryParse(envId, out var parsed))
            _apiId = parsed;
        else
            _apiId = config.Config.ApiId;

        var envHash = Environment.GetEnvironmentVariable("TG_API_HASH") ?? Environment.GetEnvironmentVariable("TELEGRAM_API_HASH");
        _apiHash = string.IsNullOrWhiteSpace(envHash) ? config.Config.ApiHash : envHash;
    }

    public void SetApiCredentials(int apiId, string apiHash)
    {
        _apiId = apiId;
        _apiHash = apiHash;
        _config.Config.ApiId = apiId;
        _config.Config.ApiHash = apiHash;
        _config.Save();
    }

    public async Task BeginLoginAsync(string phone)
    {
        EnsureApiCredentials();
        _currentPhone = phone;
        _config.Config.LastPhone = phone;
        _config.Save();

        var sessionPath = GetSessionPath(phone);
        var client = new Client(ConfigBuilder(sessionPath));
        var result = await client.Login(phone);

        if (result == "already_authorized")
        {
            var me = await client.LoginUserIfNeeded();
            Accounts.Add(TelegramAccount.FromUser(client, phone, sessionPath, me));
            return;
        }

        if (result != "verification_code" && result != "password")
            throw new InvalidOperationException($"Неожиданный ответ Telegram: {result}");

        _pending[phone] = new TelegramAccount
        {
            Client = client,
            PhoneNumber = phone,
            SessionPath = sessionPath
        };
    }

    public async Task<TelegramAccount> CompleteLoginAsync(string phone, string code, string? password)
    {
        EnsureApiCredentials();

        if (!_pending.TryGetValue(phone, out var account))
            throw new InvalidOperationException("Сначала запросите код для этого номера в Telegram.");

        var step = await account.Client.Login(code);
        if (step == "password")
        {
            if (string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException("Требуется пароль 2FA.");
            step = await account.Client.Login(password);
        }

        if (step != null)
            throw new InvalidOperationException($"Не удалось завершить вход: {step}");

        var me = await account.Client.LoginUserIfNeeded();
        var ready = TelegramAccount.FromUser(account.Client, phone, account.SessionPath, me);
        Accounts.Add(ready);
        _pending.Remove(phone);
        return ready;
    }

    public async Task<List<DialogEntry>> FetchDialogsAsync(TelegramAccount account, int limit = 30)
    {
        var dialogsResult = await account.Client.Messages_GetDialogs(limit: limit);
        var (dialogItems, users, chats) = ExtractDialogs(dialogsResult);

        var entries = new List<DialogEntry>();
        foreach (var dialog in dialogItems.Take(limit))
        {
            if (dialog.Peer == null) continue;
            var peer = BuildInputPeer(dialog.Peer, users, chats);
            if (peer == null) continue;

            var title = ResolveTitle(dialog.Peer, users, chats);
            var subtitle = ResolveSubtitle(dialog.Peer, users, chats);
            entries.Add(new DialogEntry { Peer = peer, Title = title, Subtitle = subtitle });
        }

        return entries;
    }

    public async Task<InputPeer?> ResolvePeerAsync(TelegramAccount account, DialogEntry? dialog, string? recipientInput)
    {
        if (dialog != null) return dialog.Peer;
        if (string.IsNullOrWhiteSpace(recipientInput)) return null;

        var (username, phone, inviteHash, idValue) = ParseRecipient(recipientInput);

        if (inviteHash != null)
        {
            var joined = await JoinByInviteAsync(account, inviteHash);
            if (joined != null) return joined;
        }

        if (idValue.HasValue)
        {
            var contacts = await account.Client.Contacts_GetContacts();
            var user = contacts?.users?.Values.FirstOrDefault(u => u.id == idValue.Value);
            if (user != null) return new InputPeerUser(user.id, user.access_hash);
        }

        if (!string.IsNullOrWhiteSpace(phone))
        {
            var peer = await ResolveByPhoneAsync(account, phone);
            if (peer != null) return peer;
        }

        if (!string.IsNullOrWhiteSpace(username))
        {
            var resolved = await account.Client.Contacts_ResolveUsername(username);
            if (resolved != null && resolved.peer != null)
            {
                var users = resolved.users ?? new Dictionary<long, User>();
                var chats = resolved.chats ?? new Dictionary<long, ChatBase>();
                return BuildInputPeer(resolved.peer, users, chats);
            }
        }

        return null;
    }

    public async Task SendTextMessageAsync(TelegramAccount account, InputPeer peer, string message)
    {
        await account.Client.SendMessageAsync(peer, message);
    }

    public async Task<InputPeer?> JoinByInviteAsync(TelegramAccount account, string inviteHash)
    {
        var updates = await account.Client.Messages_ImportChatInvite(inviteHash);
        var chats = updates switch
        {
            Updates upd when upd.chats != null => upd.chats,
            UpdatesCombined comb when comb.chats != null => comb.chats,
            _ => null
        };
        if (chats == null || chats.Count == 0) return null;
        var chat = chats.Values.FirstOrDefault();
        return chat switch
        {
            Channel ch => new InputPeerChannel(ch.ID, ch.access_hash),
            Chat c => new InputPeerChat(c.ID),
            _ => null
        };
    }

    private async Task<InputPeer?> ResolveByPhoneAsync(TelegramAccount account, string phone)
    {
        var contacts = await account.Client.Contacts_GetContacts();
        var existing = contacts?.users?.Values.FirstOrDefault(u => NormalizePhone(u.phone) == NormalizePhone(phone));
        if (existing != null) return new InputPeerUser(existing.id, existing.access_hash);

        var import = await account.Client.Contacts_ImportContacts(new[] { new InputPhoneContact { phone = phone, first_name = " ", last_name = string.Empty } });
        var imported = import?.users?.Values.OfType<User>().FirstOrDefault();
        if (imported != null) return new InputPeerUser(imported.id, imported.access_hash);
        return null;
    }

    public void RemoveAccount(TelegramAccount account)
    {
        if (Accounts.Contains(account)) Accounts.Remove(account);
        try { account.Client?.Dispose(); } catch { /* ignore */ }
        if (File.Exists(account.SessionPath))
        {
            try { File.Delete(account.SessionPath); } catch { /* ignore */ }
        }
    }

    public async Task RestoreSessionsAsync()
    {
        if (!_apiId.HasValue || string.IsNullOrWhiteSpace(_apiHash)) return;

        var files = Directory.GetFiles(_sessionsDir, "*.session");
        foreach (var file in files)
        {
            var phone = Path.GetFileNameWithoutExtension(file) ?? string.Empty;
            var client = new Client(ConfigBuilder(file));
            var me = await client.LoginUserIfNeeded();
            if (me == null) continue;

            Accounts.Add(TelegramAccount.FromUser(client, phone, file, me));
        }
    }

    private Func<string, string?> ConfigBuilder(string sessionPath)
    {
        return what =>
        {
            return what switch
            {
                "session_pathname" => sessionPath,
                "api_id" => _apiId?.ToString(),
                "api_hash" => _apiHash,
                "phone_number" => _currentPhone ?? _config.Config.LastPhone,
                _ => null
            };
        };
    }

    private static (string? Username, string? Phone, string? Invite, long? Id) ParseRecipient(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, null, null, null);
        var text = raw.Trim();
        if (long.TryParse(text, out var id)) return (null, null, null, id);

        if (text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            text = text["https://".Length..];
        else if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            text = text["http://".Length..];
        text = text.TrimStart('/');
        if (text.StartsWith("t.me/", StringComparison.OrdinalIgnoreCase))
            text = text["t.me/".Length..];

        if (text.StartsWith("joinchat/", StringComparison.OrdinalIgnoreCase))
            text = text["joinchat/".Length..];

        if (text.StartsWith("+"))
            text = text[1..];

        if (text.StartsWith("@"))
            text = text[1..];

        if (raw.Contains("joinchat", StringComparison.OrdinalIgnoreCase) || raw.Contains("+"))
            return (null, null, text, null);

        if (text.All(char.IsDigit))
            return (null, text, null, null);

        return (text, null, null, null);
    }

    private static string NormalizePhone(string? phone)
    {
        return phone == null ? string.Empty : new string(phone.Where(char.IsDigit).ToArray());
    }

    private static (IEnumerable<DialogBase> Dialogs, Dictionary<long, User> Users, Dictionary<long, ChatBase> Chats) ExtractDialogs(Messages_DialogsBase dialogs)
    {
        if (dialogs is Messages_Dialogs full)
            return (full.dialogs ?? Array.Empty<DialogBase>(),
                full.users ?? new Dictionary<long, User>(),
                full.chats ?? new Dictionary<long, ChatBase>());

        if (dialogs is Messages_DialogsSlice slice)
            return (slice.dialogs ?? Array.Empty<DialogBase>(),
                slice.users ?? new Dictionary<long, User>(),
                slice.chats ?? new Dictionary<long, ChatBase>());

        return (Array.Empty<DialogBase>(), new Dictionary<long, User>(), new Dictionary<long, ChatBase>());
    }

    private string GetSessionPath(string phone)
    {
        var safe = new string(phone.Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(safe)) safe = Guid.NewGuid().ToString("N");
        return Path.Combine(_sessionsDir, $"{safe}.session");
    }

    private static InputPeer? BuildInputPeer(Peer peer, IDictionary<long, User> users, IDictionary<long, ChatBase> chats)
    {
        return peer switch
        {
            PeerUser u when users.TryGetValue(u.user_id, out var user) => new InputPeerUser(user.id, user.access_hash),
            PeerChat c when chats.TryGetValue(c.chat_id, out var chat) => new InputPeerChat(chat.ID),
            PeerChannel ch when chats.TryGetValue(ch.channel_id, out var channel) && channel is Channel realChannel => new InputPeerChannel(realChannel.ID, realChannel.access_hash),
            _ => null
        };
    }

    private static string ResolveTitle(Peer peer, IDictionary<long, User> users, IDictionary<long, ChatBase> chats)
    {
        return peer switch
        {
            PeerUser u when users.TryGetValue(u.user_id, out var user) => FormatName(user),
            PeerChat c when chats.TryGetValue(c.chat_id, out var chat) => chat.Title,
            PeerChannel ch when chats.TryGetValue(ch.channel_id, out var chat) => chat.Title,
            _ => "Не удалось определить имя"
        };
    }

    private static string ResolveSubtitle(Peer peer, IDictionary<long, User> users, IDictionary<long, ChatBase> chats)
    {
        return peer switch
        {
            PeerUser u when users.TryGetValue(u.user_id, out var user) => string.IsNullOrWhiteSpace(user.username) ? $"id {user.id}" : $"@{user.username}",
            PeerChat c when chats.TryGetValue(c.chat_id, out var chat) => $"группа | id {chat.ID}",
            PeerChannel ch when chats.TryGetValue(ch.channel_id, out var chat) => $"канал | id {chat.ID}",
            _ => string.Empty
        };
    }

    private static string FormatName(User user)
    {
        var name = $"{user.first_name} {user.last_name}".Trim();
        return string.IsNullOrWhiteSpace(name) ? user.username ?? $"id {user.id}" : name;
    }

    private void EnsureApiCredentials()
    {
        if (!_apiId.HasValue || string.IsNullOrWhiteSpace(_apiHash))
            throw new InvalidOperationException("Укажите API ID и API Hash.");
    }
}
