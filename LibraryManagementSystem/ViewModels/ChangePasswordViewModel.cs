using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibraryManagementSystem.Services;

namespace LibraryManagementSystem.ViewModels;

public partial class ChangePasswordViewModel(AuthenticationService authentication) : ObservableObject
{
    [ObservableProperty] private string message = "Use at least 12 characters with upper/lowercase, a number, and a symbol.";
    [ObservableProperty] private bool isError;
    public Func<string>? CurrentPasswordProvider { get; set; }
    public Func<string>? NewPasswordProvider { get; set; }
    public Func<string>? ConfirmationProvider { get; set; }
    public Action<bool>? Close { get; set; }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var newPassword = NewPasswordProvider?.Invoke() ?? string.Empty;
        if (newPassword != (ConfirmationProvider?.Invoke() ?? string.Empty))
        {
            IsError = true;
            Message = "The new password and confirmation do not match.";
            return;
        }
        var result = await authentication.ChangePasswordAsync(CurrentPasswordProvider?.Invoke() ?? string.Empty, newPassword);
        IsError = !result.Success;
        Message = result.Message;
        if (result.Success) Close?.Invoke(true);
    }
}
