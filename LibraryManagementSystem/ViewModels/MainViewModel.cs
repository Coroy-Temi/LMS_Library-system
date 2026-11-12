using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibraryManagementSystem.Services;
using LibraryManagementSystem.Views;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;

namespace LibraryManagementSystem.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IServiceProvider services;
    private readonly AuthorizationService authorization;
    private readonly ThemeService theme;
    [ObservableProperty] private object? currentView;
    [ObservableProperty] private string pageTitle = "Dashboard";
    [ObservableProperty] private bool isDark;
    [ObservableProperty] private string displayName = "User";

    public string RoleName { get; }
    public bool IsStaff { get; }
    public bool IsViewer { get; }
    public bool HasMemberProfile { get; }
    public bool CanChangePassword { get; }
    public bool CanManageCatalog => authorization.Has(Permission.ManageCatalog);
    public bool CanViewMembers => authorization.Has(Permission.ViewMembers);
    public bool CanManageCirculation => authorization.Has(Permission.ManageCirculation);
    public bool CanManageReservations => authorization.Has(Permission.ManageReservations);
    public bool CanOpenAdmin => authorization.Has(Permission.ManageUsers);
    public Func<Task>? LogoutRequested { get; set; }
    public Action? ChangePasswordRequested { get; set; }

    public MainViewModel(IServiceProvider services, SessionService session, AuthorizationService authorization, ThemeService theme)
    {
        this.services = services;
        this.authorization = authorization;
        this.theme = theme;
        DisplayName = session.Current?.DisplayName ?? "User";
        IsDark = theme.IsDark;
        theme.ThemeChanged += (_, _) => IsDark = theme.IsDark;
        session.IdentityChanged += (_, _) => DisplayName = session.Current?.DisplayName ?? "User";
        RoleName = session.Current?.Role.ToString() ?? string.Empty;
        IsViewer = session.Current?.Role == Models.UserRole.Viewer;
        IsStaff = !IsViewer;
        HasMemberProfile = session.Current?.MemberId is not null;
        CanChangePassword = session.Current?.HasLocalPassword == true;
        Navigate(HasMemberProfile ? "My Library" : "Dashboard");
    }

    [RelayCommand]
    private void Navigate(string page)
    {
        var permission = page switch
        {
            "Books" => Permission.ViewCatalog,
            "Catalog" => Permission.ViewCatalog,
            "My Library" => Permission.ViewDashboard,
            "Settings" => Permission.ViewDashboard,
            "Members" => Permission.ViewMembers,
            "Loans" => Permission.ManageCirculation,
            "Reservations" => Permission.ManageReservations,
            "Admin" => Permission.ManageUsers,
            _ => Permission.ViewDashboard
        };
        if (!authorization.Has(permission))
        {
            MessageBox.Show("Your role does not permit access to this area.", "Access denied",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        PageTitle = page;
        CurrentView = page switch
        {
            "Books" => services.GetRequiredService<BooksView>(),
            "Catalog" => services.GetRequiredService<BrowseCatalogView>(),
            "My Library" => services.GetRequiredService<MemberPortalView>(),
            "Settings" => services.GetRequiredService<SettingsView>(),
            "Members" => services.GetRequiredService<MembersView>(),
            "Loans" => services.GetRequiredService<LoansView>(),
            "Reservations" => services.GetRequiredService<ReservationsView>(),
            "Admin" => services.GetRequiredService<AdminView>(),
            _ => services.GetRequiredService<DashboardView>()
        };
    }

    [RelayCommand] private Task LogoutAsync() => LogoutRequested?.Invoke() ?? Task.CompletedTask;
    [RelayCommand] private void ChangePassword() => ChangePasswordRequested?.Invoke();

    [RelayCommand]
    private void ToggleTheme()
    {
        theme.Toggle();
    }
}
