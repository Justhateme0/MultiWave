using System;
using System.Windows;
using MultiWave.Services;

namespace MultiWave;

public partial class App : System.Windows.Application
{
    public AppConfigService ConfigService { get; } = new();

    private void OnStartup(object sender, StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var authWindow = new AuthWindow(ConfigService);
        var result = authWindow.ShowDialog();
        if (result != true)
        {
            Shutdown();
            return;
        }

        try
        {
            var main = new MainWindow(ConfigService);
            MainWindow = main;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            main.Show();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "Ошибка запуска", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }
}
