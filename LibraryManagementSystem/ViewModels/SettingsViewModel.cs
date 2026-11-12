using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibraryManagementSystem.Services;

namespace LibraryManagementSystem.ViewModels;

public partial class SettingsViewModel(
    MemberPortalService portal,
    SessionService session,
    ThemeService theme,
    IFileDialogService dialogs,
    ProfileImageStorageService imageStorage) : ObservableObject
{
    [ObservableProperty] private string profileName = string.Empty;
    [ObservableProperty] private string profileEmail = string.Empty;
    [ObservableProperty] private string profilePhone = string.Empty;
    [ObservableProperty] private string profileAddress = string.Empty;
    [ObservableProperty] private string? profileImagePath;
    [ObservableProperty] private string message = string.Empty;
    [ObservableProperty] private bool isDarkMode = theme.IsDark;
    [ObservableProperty] private bool isBusy;
    public bool HasMemberProfile => session.Current?.MemberId is not null;
    public bool CanChangePassword => session.Current?.HasLocalPassword == true;
    public Action? ChangePasswordRequested { get; set; }

    partial void OnIsDarkModeChanged(bool value) => theme.Apply(value);

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsDarkMode = theme.IsDark;
        if (!HasMemberProfile) return;
        var snapshot = await portal.GetAsync();
        ProfileName = snapshot.Member.FullName;
        ProfileEmail = snapshot.Member.Email;
        ProfilePhone = snapshot.Member.Phone ?? string.Empty;
        ProfileAddress = snapshot.Member.Address ?? string.Empty;
        ProfileImagePath = snapshot.Member.PhotoPath;
    }

    [RelayCommand]
    private async Task SaveProfileAsync()
    {
        if (IsBusy || !HasMemberProfile) return;
        IsBusy = true;
        try
        {
            var result = await portal.UpdateProfileAsync(ProfileName, ProfilePhone, ProfileAddress);
            Message = result.Message;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ChangePhotoAsync()
    {
        if (IsBusy || !HasMemberProfile) return;
        var source = dialogs.PickProfileImage();
        if (source is null) return;
        IsBusy = true;
        try
        {
            var update = await portal.UpdateProfileImageAsync(source, imageStorage);
            Message = update.Result.Message;
            if (update.Result.Success) ProfileImagePath = update.ImagePath;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand] private void ChangePassword() => ChangePasswordRequested?.Invoke();
}
