using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibraryManagementSystem.Services;

namespace LibraryManagementSystem.ViewModels;

public partial class RegistrationViewModel(AccountRegistrationService registration) : ObservableObject
{
    [ObservableProperty] private string fullName = string.Empty;
    [ObservableProperty] private string email = string.Empty;
    [ObservableProperty] private string username = string.Empty;
    [ObservableProperty] private string phone = string.Empty;
    [ObservableProperty] private string address = string.Empty;
    [ObservableProperty] private string verificationCode = string.Empty;
    [ObservableProperty] private bool isVerificationStep;
    [ObservableProperty] private string message = "Create a member account to browse the catalog and view your loans.";
    [ObservableProperty] private bool isBusy;
    public bool IsGoogleConfigured => registration.IsGoogleConfigured;
    public Func<string>? PasswordProvider { get; set; }
    public Func<string>? ConfirmationProvider { get; set; }
    public Action<bool>? Close { get; set; }

    [RelayCommand]
    private async Task RegisterAsync()
    {
        if (IsBusy) return;
        var password = PasswordProvider?.Invoke() ?? string.Empty;
        if (password != (ConfirmationProvider?.Invoke() ?? string.Empty))
        {
            Message = "The password and confirmation do not match.";
            return;
        }
        IsBusy = true;
        try
        {
            var result = await registration.RegisterLocalAsync(new(
                FullName, Email, Username, password, Phone, Address));
            Message = result.Message;
            if (result.Success && result.RequiresEmailVerification) IsVerificationStep = true;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task GoogleRegisterAsync()
    {
        if (IsBusy) return;
        Message = IsGoogleConfigured ? "Opening your browser for Google registration..." : string.Empty;
        IsBusy = true;
        try
        {
            var result = await registration.SignInOrRegisterWithGoogleAsync();
            Message = result.Message;
            if (result.Success) Close?.Invoke(true);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task VerifyEmailAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var result = await registration.VerifyEmailAsync(Email, VerificationCode);
            Message = result.Message;
            if (result.Success) Close?.Invoke(true);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ResendCodeAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var result = await registration.ResendVerificationCodeAsync(Email);
            Message = result.Message;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void ShowVerification()
    {
        IsVerificationStep = true;
        Message = "Enter your registration email and the six-digit code we sent you.";
    }

    [RelayCommand]
    private void BackToRegistration()
    {
        IsVerificationStep = false;
        Message = "Create a member account to browse the catalog and view your loans.";
    }
}
