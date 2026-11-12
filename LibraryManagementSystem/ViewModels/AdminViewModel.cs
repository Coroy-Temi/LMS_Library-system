using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using Microsoft.Win32;

namespace LibraryManagementSystem.ViewModels;

public partial class AdminViewModel : ObservableObject
{
    private readonly UserAdministrationService users;
    private readonly BackupService backup;
    private readonly AuditService audit;

    public ObservableCollection<User> Users { get; } = [];
    public ObservableCollection<AuditLog> AuditLogs { get; } = [];
    public IEnumerable<UserRole> Roles => Enum.GetValues<UserRole>();

    [ObservableProperty] private User? selectedUser;
    [ObservableProperty] private string username = string.Empty;
    [ObservableProperty] private string displayName = string.Empty;
    [ObservableProperty] private string password = string.Empty;
    [ObservableProperty] private UserRole role = UserRole.Librarian;

    public AdminViewModel(UserAdministrationService users, BackupService backup, AuditService audit)
    {
        this.users = users;
        this.backup = backup;
        this.audit = audit;
        _ = LoadAsync();
        _ = LoadAuditLogsAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        Users.Clear();
        foreach (var item in await users.ListAsync())
            Users.Add(item);
    }

    [RelayCommand]
    private async Task LoadAuditLogsAsync()
    {
        AuditLogs.Clear();
        foreach (var item in await audit.GetRecentAsync())
            AuditLogs.Add(item);
    }

    [RelayCommand]
    private async Task CreateUserAsync()
    {
        try
        {
            await users.CreateAsync(Username, DisplayName, Role, Password);
            Username = DisplayName = Password = string.Empty;
            await LoadAsync();
            await LoadAuditLogsAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "User management");
        }
    }

    [RelayCommand]
    private async Task ResetPasswordAsync()
    {
        if (SelectedUser is null || string.IsNullOrWhiteSpace(Password)) return;
        try
        {
            await users.ResetPasswordAsync(SelectedUser.UserId, Password);
            Password = string.Empty;
            await LoadAsync();
            await LoadAuditLogsAsync();
            MessageBox.Show("Password reset. The user must change it at the next sign-in.");
        }
        catch (Exception exception) { MessageBox.Show(exception.Message, "User management"); }
    }

    [RelayCommand]
    private async Task ToggleActiveAsync()
    {
        if (SelectedUser is null) return;
        try
        {
            await users.SetActiveAsync(SelectedUser.UserId, !SelectedUser.IsActive);
            await LoadAsync();
            await LoadAuditLogsAsync();
        }
        catch (Exception exception) { MessageBox.Show(exception.Message, "User management"); }
    }

    [RelayCommand]
    private async Task UnlockAsync()
    {
        if (SelectedUser is null) return;
        try
        {
            await users.UnlockAsync(SelectedUser.UserId);
            await LoadAsync();
            await LoadAuditLogsAsync();
        }
        catch (Exception exception) { MessageBox.Show(exception.Message, "User management"); }
    }

    [RelayCommand]
    private async Task BackupAsync()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "SQLite database|*.db",
            FileName = $"LibraryForge-{DateTime.Now:yyyyMMdd}.db"
        };
        if (dialog.ShowDialog() != true) return;
        await backup.BackupAsync(dialog.FileName);
        await audit.RecordAsync("DatabaseBackedUp", "Database", details: $"Destination={dialog.FileName}");
        await LoadAuditLogsAsync();
        MessageBox.Show("Backup completed.");
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        var dialog = new OpenFileDialog { Filter = "SQLite database|*.db" };
        if (dialog.ShowDialog() != true || MessageBox.Show(
                "Restore will replace the current database. Continue?", "Restore",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await backup.RestoreAsync(dialog.FileName);
        MessageBox.Show("Restore completed. Restart LibraryForge.");
    }
}
