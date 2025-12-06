using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using MultiWave.Models;
using MultiWave.Services;
using TL;
using WTelegram;
using System.Windows.Forms;

namespace MultiWave;

public partial class MainWindow : Window
{
    private readonly TelegramService _service;
    private readonly AppConfigService _config;
    private readonly NotifyIcon _notify;

    public ObservableCollection<TelegramAccount> Accounts => _service.Accounts;
    public ObservableCollection<DialogEntry> Dialogs { get; } = new();

    public MainWindow(AppConfigService config)
    {
        _config = config;
        _service = new TelegramService(config);

        InitializeComponent();
        DataContext = this;
        _notify = new NotifyIcon
        {
            Visible = true,
            Icon = System.Drawing.SystemIcons.Information,
            Text = "MultiWave"
        };

        ApplyConfig();
        Loaded += OnLoaded;
        Closed += (_, _) => _notify.Dispose();
    }

    private void ApplyConfig()
    {
        ApiIdBox.Text = _config.Config.ApiId?.ToString() ?? string.Empty;
        ApiHashBox.Text = _config.Config.ApiHash ?? string.Empty;
        PhoneBox.Text = _config.Config.LastPhone ?? string.Empty;
        ToggleApiSection();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await SafeRunAsync(_service.RestoreSessionsAsync, "Сессии восстановлены");
    }

    private async Task SafeRunAsync(Func<Task> action, string successMessage = "")
    {
        try
        {
            await action();
            if (!string.IsNullOrWhiteSpace(successMessage)) SetStatus(successMessage);
        }
        catch (RpcException ex)
        {
            SetStatus(ex.Message, true);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, true);
        }
    }

    private void SetStatus(string message, bool isError = false)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        var brush = (System.Windows.Media.Brush)FindResource(isError ? "AccentBrush" : "MutedBrush");
        StatusText.Foreground = brush;
        StatusText.Text = message;

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
        StatusText.BeginAnimation(OpacityProperty, fade);
    }

    private async void SaveApiButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(ApiIdBox.Text, out var apiId) || string.IsNullOrWhiteSpace(ApiHashBox.Text))
        {
            SetStatus("Введите корректные API ID и API Hash.", true);
            return;
        }

        _service.SetApiCredentials(apiId, ApiHashBox.Text.Trim());
        _config.Config.ApiId = apiId;
        _config.Config.ApiHash = ApiHashBox.Text.Trim();
        _config.Save();

        await SafeRunAsync(async () =>
        {
            Dialogs.Clear();
            if (!Accounts.Any()) await _service.RestoreSessionsAsync();
        }, "API сохранены");
        ToggleApiSection();
    }

    private async void SendCodeButton_OnClick(object sender, RoutedEventArgs e)
    {
        var phone = PhoneBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(phone))
        {
            SetStatus("Введите номер телефона.", true);
            return;
        }

        _config.Config.LastPhone = phone;
        _config.Save();
        await SafeRunAsync(() => _service.BeginLoginAsync(phone), "Код отправлен");
    }

    private async void ConfirmButton_OnClick(object sender, RoutedEventArgs e)
    {
        var phone = PhoneBox.Text.Trim();
        var code = CodeBox.Text.Trim();
        var password = PasswordBox.Password;

        if (string.IsNullOrWhiteSpace(phone) || string.IsNullOrWhiteSpace(code))
        {
            SetStatus("Введите телефон и код.", true);
            return;
        }

        await SafeRunAsync(async () =>
        {
            await _service.CompleteLoginAsync(phone, code, password);
            CodeBox.Clear();
            PasswordBox.Clear();
        }, "Аккаунт добавлен");
    }

    private void SelectAllAccountsButton_OnClick(object sender, RoutedEventArgs e)
    {
        AccountsList.SelectAll();
    }

    private TelegramAccount? GetSelectedAccount()
    {
        return AccountsList.SelectedItem as TelegramAccount;
    }

    private List<TelegramAccount> GetSelectedAccounts()
    {
        return AccountsList.SelectedItems.Cast<TelegramAccount>().ToList();
    }

    private async void RefreshDialogsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var account = GetSelectedAccount() ?? Accounts.FirstOrDefault();
        if (account == null)
        {
            SetStatus("Нет доступных аккаунтов.", true);
            return;
        }

        await SafeRunAsync(async () =>
        {
            Dialogs.Clear();
            var items = await _service.FetchDialogsAsync(account);
            foreach (var item in items) Dialogs.Add(item);
        }, "Диалоги обновлены");
    }

    private async void SendButton_OnClick(object sender, RoutedEventArgs e)
    {
        var message = MessageInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(message))
        {
            SetStatus("Введите текст сообщения.", true);
            return;
        }

        var accounts = GetSelectedAccounts();
        if (accounts.Count == 0) accounts = Accounts.ToList();

        if (accounts.Count == 0)
        {
            SetStatus("Добавьте хотя бы один аккаунт.", true);
            return;
        }

        var dialog = DialogsList.SelectedItem as DialogEntry;
        var recipient = RecipientBox.Text.Trim();
        var repeat = ParseIntOrDefault(RepeatCountBox.Text, 1);
        var delay = ParseIntOrDefault(DelayBox.Text, 1000);
        if (repeat < 1) repeat = 1;
        if (delay < 0) delay = 0;

        ShowNotification("Рассылка запущена", $"Повторов: {repeat}");
        await SafeRunAsync(async () =>
        {
            for (var i = 0; i < repeat; i++)
            {
                foreach (var account in accounts)
                {
                    var peer = await _service.ResolvePeerAsync(account, dialog, recipient);
                    if (peer == null) throw new InvalidOperationException("Не удалось определить получателя.");
                    await _service.SendTextMessageAsync(account, peer, message);
                }
                if (i < repeat - 1 && delay > 0) await Task.Delay(delay);
            }
        }, "Рассылка завершена");
        ShowNotification("Рассылка завершена", $"Отправлено циклов: {repeat}");
    }

    private async void SendComplaintButton_OnClick(object sender, RoutedEventArgs e)
    {
        var accounts = GetSelectedAccounts();
        if (accounts.Count == 0) accounts = Accounts.ToList();
        if (accounts.Count == 0)
        {
            SetStatus("Добавьте хотя бы один аккаунт.", true);
            return;
        }

        var target = ComplaintTargetBox.Text.Trim();
        var dialog = DialogsList.SelectedItem as DialogEntry;
        if (string.IsNullOrWhiteSpace(target) && dialog == null)
        {
            SetStatus("Укажите цель жалобы или выберите диалог.", true);
            return;
        }

        var reasonText = ComplaintReasonBox.Text.Trim();
        var reasonKey = ComplaintReasonSelect.SelectedValue as string ?? "spam";
        var repeat = ParseIntOrDefault(ComplaintRepeatBox.Text, 1);
        if (repeat < 1) repeat = 1;

        ShowNotification("Жалоба", "Отправка жалобы запущена");
        await SafeRunAsync(async () =>
        {
            for (var i = 0; i < repeat; i++)
            {
                foreach (var account in accounts)
                {
                    var peer = await _service.ResolvePeerAsync(account, dialog, target);
                    if (peer == null) throw new InvalidOperationException("Не удалось определить цель жалобы.");
                    var reason = ResolveReportReason(reasonKey);
                    await account.Client.Account_ReportPeer(peer, reason, reasonText ?? string.Empty);
                }
            }
        }, "Жалоба отправлена");
        ShowNotification("Жалоба", "Отправка завершена");
    }

    private void DeleteAccountsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var accounts = GetSelectedAccounts();
        if (accounts.Count == 0)
        {
            SetStatus("Выберите аккаунты для удаления.", true);
            return;
        }

        foreach (var account in accounts.ToList())
        {
            _service.RemoveAccount(account);
        }

        Dialogs.Clear();
        SetStatus("Аккаунты удалены");
    }

    private void ToggleApiSection()
    {
        var hasApi = _config.Config.ApiId.HasValue && !string.IsNullOrWhiteSpace(_config.Config.ApiHash);
        ApiInputs.Visibility = hasApi ? Visibility.Collapsed : Visibility.Visible;
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void MinimizeButton_OnClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private static int ParseIntOrDefault(string? text, int fallback)
    {
        return int.TryParse(text, out var value) ? value : fallback;
    }

    private void ShowNotification(string title, string message)
    {
        _notify.BalloonTipTitle = title;
        _notify.BalloonTipText = message;
        _notify.ShowBalloonTip(2000);
    }

    private static ReportReason ResolveReportReason(string key)
    {
        return key switch
        {
            "violence" => ReportReason.Violence,
            "porn" => ReportReason.Pornography,
            "other" => ReportReason.Other,
            _ => ReportReason.Spam
        };
    }
}
