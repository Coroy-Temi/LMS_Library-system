using LibraryManagementSystem.ViewModels;
using LibraryManagementSystem.Views;
using LibraryManagementSystem.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Threading;

namespace LibraryManagementSystem;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;
    private readonly SessionService session;
    private readonly DispatcherTimer sessionTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private bool switchingUser;

    public MainWindow(MainViewModel viewModel, SessionService session)
    {
        InitializeComponent();
        DataContext = this.viewModel = viewModel;
        this.session = session;
        viewModel.LogoutRequested = LogoutAsync;
        viewModel.ChangePasswordRequested = ShowChangePassword;
        sessionTimer.Tick += SessionTimerOnTick;
        sessionTimer.Start();
    }

    private async Task LogoutAsync()
    {
        switchingUser = true;
        sessionTimer.Stop();
        await ((App)Application.Current).ReturnToLoginAsync(this);
    }

    private void ShowChangePassword()
    {
        var dialog = ((App)Application.Current).Services.GetRequiredService<ChangePasswordWindow>();
        dialog.Owner = this;
        dialog.ShowDialog();
    }

    private async void SessionTimerOnTick(object? sender, EventArgs e)
    {
        if (!session.IsExpired) return;
        MessageBox.Show("Your session expired. Please sign in again.", "Session expired");
        await LogoutAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        sessionTimer.Stop();
        base.OnClosed(e);
        if (!switchingUser) Application.Current.Shutdown();
    }
}
