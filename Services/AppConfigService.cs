using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MultiWave.Models;

namespace MultiWave.Services;

public class AppConfigService
{
    private readonly string _path;
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly byte[] Entropy = SHA256.HashData(Encoding.UTF8.GetBytes("MultiWave::ConfigSalt"));

    public AppConfig Config { get; private set; } = new();

    public AppConfigService(string? path = null)
    {
        _path = path ?? Path.Combine(AppContext.BaseDirectory, "config.json");
        Load();
    }

    public void Load()
    {
        if (!File.Exists(_path))
        {
            Config = new AppConfig();
            return;
        }

        try
        {
            var json = File.ReadAllText(_path);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("Data", out var dataProp))
            {
                var cipher = dataProp.GetString();
                if (!string.IsNullOrWhiteSpace(cipher))
                {
                    var plain = Decrypt(cipher);
                    Config = JsonSerializer.Deserialize<AppConfig>(plain, Options) ?? new AppConfig();
                    return;
                }
            }

            Config = JsonSerializer.Deserialize<AppConfig>(json, Options) ?? new AppConfig();
        }
        catch
        {
            Config = new AppConfig();
        }
    }

    public void Save()
    {
        var plainJson = JsonSerializer.Serialize(Config, Options);
        var encrypted = Encrypt(plainJson);
        var wrapped = JsonSerializer.Serialize(new { Data = encrypted }, Options);
        File.WriteAllText(_path, wrapped);
    }

    private static string Encrypt(string plain)
    {
        var bytes = Encoding.UTF8.GetBytes(plain);
        var protectedBytes = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    private static string Decrypt(string base64)
    {
        var data = Convert.FromBase64String(base64);
        var plain = ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plain);
    }
}
