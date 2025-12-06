using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using MultiWave.Services;

namespace MultiWave;

public partial class AuthWindow : Window
{
    private readonly AppConfigService _config;
    private readonly SupabaseAuthService _supabase;
    private bool _dialogReady;

    public AuthWindow(AppConfigService config)
    {
        _config = config;
        _supabase = new SupabaseAuthService(config);
        InitializeComponent();
        LoadConfig();
        Loaded += (_, _) => _dialogReady = true;
    }

    private void LoadConfig()
    {
        UsernameBox.Text = _config.Config.Username ?? string.Empty;
    }

    private async void LoginButton_OnClick(object sender, RoutedEventArgs e)
    {
        await AuthAsync(isRegister: false);
    }

    private async void RegisterButton_OnClick(object sender, RoutedEventArgs e)
    {
        await AuthAsync(isRegister: true);
    }

    private async Task AuthAsync(bool isRegister)
    {
        var username = UsernameBox.Text.Trim();
        var password = PasswordBox.Password;
        _config.Config.Username = username;
        _config.Save();

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            SetStatus("Введите логин и пароль.", true);
            return;
        }

        SetStatus(isRegister ? "Регистрируем..." : "Входим...");
        var result = isRegister
            ? await _supabase.RegisterAsync(username, password)
            : await _supabase.LoginAsync(username, password);

        if (!result.ok)
        {
            SetStatus(result.error ?? "Ошибка авторизации", true);
            return;
        }

        _config.Config.SupabaseAccessToken = result.token;
        _config.Save();
        SetDialogResultAndClose(true);
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        SetDialogResultAndClose(false);
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void SetStatus(string message, bool isError = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = (System.Windows.Media.Brush)FindResource(isError ? "AccentBrush" : "MutedBrush");
    }

    private void SetDialogResultAndClose(bool result)
    {
        try
        {
            if (_dialogReady) DialogResult = result;
        }
        catch
        {
        }
        Close();
    }
}
