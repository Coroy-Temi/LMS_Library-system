using LibraryManagementSystem.Data;
using LibraryManagementSystem.Services;
using LibraryManagementSystem.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Windows;

namespace LibraryManagementSystem;

public partial class App : Application
{
    private IHost? host;
    public IServiceProvider Services => host?.Services ?? throw new InvalidOperationException("Application host is unavailable.");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            host = Program.CreateHostBuilder(e.Args).Build();
            await host.StartAsync();
            Services.GetRequiredService<ThemeService>().ApplySavedTheme();
            await Services.GetRequiredService<DatabaseInitializer>().InitializeAsync();
            ShowLoginOrExit();
        }
        catch (Exception exception)
        {
            MessageBox.Show($"LibraryForge could not start.\n\n{exception.Message}", "Startup error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private void ShowLoginOrExit()
    {
        var login = Services.GetRequiredService<LoginWindow>();
        if (login.ShowDialog() != true)
        {
            Shutdown();
            return;
        }

        var session = Services.GetRequiredService<SessionService>();
        if (session.Current?.MustChangePassword == true)
        {
            var changePassword = Services.GetRequiredService<ChangePasswordWindow>();
            if (changePassword.ShowDialog() != true)
            {
                Services.GetRequiredService<AuthenticationService>().LogoutAsync().GetAwaiter().GetResult();
                ShowLoginOrExit();
                return;
            }
        }
        Services.GetRequiredService<MainWindow>().Show();
    }

    internal async Task ReturnToLoginAsync(MainWindow shell)
    {
        await Services.GetRequiredService<AuthenticationService>().LogoutAsync();
        shell.Close();
        ShowLoginOrExit();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (host is not null)
        {
            await host.StopAsync();
            host.Dispose();
        }
        base.OnExit(e);
    }
}
