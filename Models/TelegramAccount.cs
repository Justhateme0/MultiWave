using WTelegram;
using TL;

namespace MultiWave.Models;

public class TelegramAccount
{
    public string PhoneNumber { get; init; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Username { get; set; }
    public long UserId { get; set; }
    public string SessionPath { get; init; } = string.Empty;
    public Client Client { get; init; } = null!;
    public bool IsAuthorized { get; set; }

    public string DisplayLabel
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(DisplayName))
                return $"{DisplayName} - {PhoneNumber}";
            if (!string.IsNullOrWhiteSpace(Username))
                return $"@{Username} - {PhoneNumber}";
            return PhoneNumber;
        }
    }

    public static TelegramAccount FromUser(Client client, string phone, string sessionPath, User self)
    {
        return new TelegramAccount
        {
            Client = client,
            PhoneNumber = phone,
            SessionPath = sessionPath,
            DisplayName = $"{self.first_name} {self.last_name}".Trim(),
            Username = self.username,
            UserId = self.id,
            IsAuthorized = true
        };
    }
}
