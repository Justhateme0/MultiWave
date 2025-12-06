using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using MultiWave.Models;

namespace MultiWave.Services;

public class SupabaseAuthService
{
    private readonly HttpClient _http = new();
    private readonly AppConfigService _config;

    public SupabaseAuthService(AppConfigService config)
    {
        _config = config;
    }

    public async Task<(bool ok, string? token, string? error)> LoginAsync(string username, string password)
    {
        return await SendAuthAsync("token?grant_type=password", new { email = username, password });
    }

    public async Task<(bool ok, string? token, string? error)> RegisterAsync(string username, string password)
    {
        return await SendAuthAsync("signup", new { email = username, password });
    }

    private async Task<(bool ok, string? token, string? error)> SendAuthAsync(string endpoint, object payload)
    {
        var url = NormalizeUrl(_config.Config.SupabaseUrl ?? Environment.GetEnvironmentVariable("SUPABASE_URL"));
        var key = _config.Config.SupabaseKey ?? Environment.GetEnvironmentVariable("SUPABASE_KEY");

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
            return (false, null, "Укажите Supabase URL/Key в config.json или через переменные SUPABASE_URL/SUPABASE_KEY.");

        var request = new HttpRequestMessage(HttpMethod.Post, $"{url}/auth/v1/{endpoint}")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add("apikey", key);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);

        var response = await _http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            var error = TryParseError(body) ?? $"{response.StatusCode}: {response.ReasonPhrase} ({body})";
            return (false, null, error);
        }

        var token = TryParseToken(body);
        return (true, token, null);
    }

    private static string? NormalizeUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var url = raw.Trim().TrimEnd('/');
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            url = $"https://{url}";
        if (url.EndsWith("/auth/v1", StringComparison.OrdinalIgnoreCase))
            url = url[..^8].TrimEnd('/');
        return url;
    }

    private static string? TryParseToken(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("access_token", out var tokenProp))
                return tokenProp.GetString();
            if (doc.RootElement.TryGetProperty("session", out var session) &&
                session.ValueKind == JsonValueKind.Object &&
                session.TryGetProperty("access_token", out var sessionToken))
                return sessionToken.GetString();
        }
        catch
        {
        }

        return null;
    }

    private static string? TryParseError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("msg", out var msg))
                return msg.GetString();
            if (doc.RootElement.TryGetProperty("error", out var err))
                return err.GetString();
            if (doc.RootElement.TryGetProperty("message", out var message))
                return message.GetString();
        }
        catch
        {
        }

        return null;
    }
}
