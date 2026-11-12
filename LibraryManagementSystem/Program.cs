using LibraryManagementSystem.Data;
using LibraryManagementSystem.Services;
using LibraryManagementSystem.ViewModels;
using LibraryManagementSystem.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.Net.Http;
namespace LibraryManagementSystem;

public static class Program
{
 public static IHostBuilder CreateHostBuilder(string[] args)=>Host.CreateDefaultBuilder(args)
  .ConfigureAppConfiguration(c=>c.SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.json",false,true))
  .UseSerilog((ctx,log)=>log.ReadFrom.Configuration(ctx.Configuration))
  .ConfigureServices((ctx,s)=>
  {
   var cs=ctx.Configuration.GetConnectionString("LibraryDatabase")!;
   s.AddPooledDbContextFactory<LibraryDbContext>(o=>o.UseSqlite(cs));
   s.AddSingleton(new HttpClient());
   s.AddSingleton<IEmailSender, SmtpEmailSender>();
   s.AddSingleton<UserPreferencesService>();s.AddSingleton<ThemeService>();s.AddSingleton<IFileDialogService, FileDialogService>();s.AddSingleton<ProfileImageStorageService>();
   s.AddSingleton<DatabaseInitializer>();s.AddSingleton<SessionService>();s.AddSingleton<AuthorizationService>();s.AddSingleton<PasswordPolicyService>();s.AddSingleton<AuditService>();s.AddSingleton<AuthenticationService>();
   s.AddSingleton<GoogleOAuthService>();s.AddSingleton<AccountRegistrationService>();s.AddSingleton<MemberPortalService>();
   s.AddSingleton<IInstallationSecretStore,WindowsInstallationSecretStore>();s.AddSingleton<SecureQrCodeService>();s.AddSingleton<InventoryService>();
   s.AddSingleton<CatalogService>();s.AddSingleton<CatalogImportService>();s.AddSingleton<MemberService>();s.AddSingleton<ReservationService>();s.AddSingleton<LoanService>();s.AddSingleton<DashboardService>();s.AddSingleton<ReportService>();s.AddSingleton<BackupService>();s.AddSingleton<UserAdministrationService>();
   s.AddTransient<LoginViewModel>();s.AddTransient<RegistrationViewModel>();s.AddTransient<ChangePasswordViewModel>();s.AddTransient<BookEditorViewModel>();s.AddTransient<MemberEditorViewModel>();s.AddTransient<BookCopiesViewModel>();s.AddTransient<SettingsViewModel>();s.AddTransient<MainViewModel>();s.AddTransient<DashboardViewModel>();s.AddTransient<MemberPortalViewModel>();s.AddTransient<BrowseCatalogViewModel>();s.AddTransient<BooksViewModel>();s.AddTransient<MembersViewModel>();s.AddTransient<LoansViewModel>();s.AddTransient<ReservationsViewModel>();s.AddTransient<AdminViewModel>();
   s.AddTransient<LoginWindow>();s.AddTransient<RegistrationWindow>();s.AddTransient<ChangePasswordWindow>();s.AddTransient<BookEditorWindow>();s.AddTransient<MemberEditorWindow>();s.AddTransient<BookCopiesWindow>();s.AddTransient<MainWindow>();s.AddTransient<SettingsView>();s.AddTransient<DashboardView>();s.AddTransient<MemberPortalView>();s.AddTransient<BrowseCatalogView>();s.AddTransient<BooksView>();s.AddTransient<MembersView>();s.AddTransient<LoansView>();s.AddTransient<ReservationsView>();s.AddTransient<AdminView>();
  });
}
