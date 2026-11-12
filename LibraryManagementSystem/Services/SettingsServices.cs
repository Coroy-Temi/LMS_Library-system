using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using System.IO;
using System.Text.Json;
using System.Windows.Media;

namespace LibraryManagementSystem.Services;

public sealed record UserPreferences(bool IsDarkMode = false);

public sealed class UserPreferencesService
{
    private readonly string path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LibraryForge", "preferences.json");

    public UserPreferences Load()
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(path)) ?? new()
                : new();
        }
        catch { return new(); }
    }

    public void Save(UserPreferences preferences)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(preferences, new JsonSerializerOptions { WriteIndented = true }));
    }
}

public sealed class ThemeService(UserPreferencesService preferences)
{
    private readonly PaletteHelper palette = new();
    public bool IsDark { get; private set; } = preferences.Load().IsDarkMode;
    public event EventHandler? ThemeChanged;

    public void ApplySavedTheme() => Apply(IsDark, false);
    public void Toggle() => Apply(!IsDark);

    public void Apply(bool isDark, bool persist = true)
    {
        IsDark = isDark;
        var theme = palette.GetTheme();
        theme.SetBaseTheme(isDark ? BaseTheme.Dark : BaseTheme.Light);
        theme.SetPrimaryColor(isDark ? Color.FromRgb(0, 168, 181) : Color.FromRgb(0, 109, 119));
        theme.SetSecondaryColor(Color.FromRgb(244, 162, 97));
        palette.SetTheme(theme);
        if (persist) preferences.Save(new(IsDark));
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }
}

public interface IFileDialogService
{
    string? PickProfileImage();
    string? PickBookCsv();
}

public sealed class FileDialogService : IFileDialogService
{
    public string? PickProfileImage()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a profile photo",
            Filter = "Supported images|*.png;*.jpg;*.jpeg;*.bmp|PNG image|*.png|JPEG image|*.jpg;*.jpeg|Bitmap image|*.bmp"
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickBookCsv()
    {
        var dialog = new OpenFileDialog { Title = "Import books from CSV", Filter = "CSV files|*.csv" };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}

public sealed class ProfileImageStorageService
{
    private static readonly HashSet<string> AllowedExtensions = [".png", ".jpg", ".jpeg", ".bmp"];
    private const long MaximumBytes = 5 * 1024 * 1024;

    public async Task<string> StoreAsync(string sourcePath, int memberId, CancellationToken cancellationToken = default)
    {
        var file = new FileInfo(sourcePath);
        var extension = file.Extension.ToLowerInvariant();
        if (!file.Exists) throw new InvalidOperationException("The selected image no longer exists.");
        if (!AllowedExtensions.Contains(extension)) throw new InvalidOperationException("Choose a PNG, JPEG, or BMP image.");
        if (file.Length > MaximumBytes) throw new InvalidOperationException("Profile images must be 5 MB or smaller.");
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LibraryForge", "ProfileImages");
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, $"member-{memberId}-{Guid.NewGuid():N}{extension}");
        await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await input.CopyToAsync(output, cancellationToken);
        return destination;
    }
}
