using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibraryManagementSystem.Services;

namespace LibraryManagementSystem.ViewModels;

public partial class LoginViewModel(
    AuthenticationService authentication,
    AccountRegistrationService registration) : ObservableObject
{
    [ObservableProperty] private string username = "admin";
    [ObservableProperty] private string errorMessage = string.Empty;
    [ObservableProperty] private bool isBusy;
    public bool IsGoogleConfigured => registration.IsGoogleConfigured;
    public Func<string>? PasswordProvider { get; set; }
    public Action<bool>? Close { get; set; }
    public Action? RegisterRequested { get; set; }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (IsBusy) return;
        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            var result = await authentication.LoginAsync(Username, PasswordProvider?.Invoke() ?? string.Empty);
            if (result.Success) Close?.Invoke(true);
            else ErrorMessage = result.LockedUntilUtc is { } locked
                ? $"{result.Message} Available after {locked.ToLocalTime():t}."
                : result.Message;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task GoogleLoginAsync()
    {
        if (IsBusy) return;
        ErrorMessage = IsGoogleConfigured ? "Opening your browser for Google sign-in..." : string.Empty;
        IsBusy = true;
        try
        {
            var result = await registration.SignInOrRegisterWithGoogleAsync();
            if (result.Success) Close?.Invoke(true); else ErrorMessage = result.Message;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand] private void Register() => RegisterRequested?.Invoke();
}
