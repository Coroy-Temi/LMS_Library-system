using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LibraryManagementSystem.Tests;

public sealed class SecurityServiceTests
{
    [Fact]
    public async Task LoginAsync_LocksAccountAfterConfiguredFailures()
    {
        await using var test = await SecurityDatabase.CreateAsync();

        await test.Authentication.LoginAsync("librarian", "wrong-one");
        await test.Authentication.LoginAsync("librarian", "wrong-two");
        var result = await test.Authentication.LoginAsync("librarian", "wrong-three");

        Assert.False(result.Success);
        Assert.NotNull(result.LockedUntilUtc);
        await using var db = await test.Factory.CreateDbContextAsync();
        Assert.True((await db.Users.SingleAsync()).LockoutEndUtc > DateTime.UtcNow);
    }

    [Fact]
    public async Task LoginAsync_CreatesImmutableRoleSessionForValidPassword()
    {
        await using var test = await SecurityDatabase.CreateAsync();

        var result = await test.Authentication.LoginAsync("librarian", "Valid@Password1");

        Assert.True(result.Success);
        Assert.True(test.Session.IsAuthenticated);
        Assert.Equal(UserRole.Librarian, test.Session.Current?.Role);
        Assert.True(test.Authorization.Has(Permission.ManageCirculation));
        Assert.False(test.Authorization.Has(Permission.ManageUsers));
    }

    [Fact]
    public void Viewer_CannotManageCirculation()
    {
        var session = new SessionService();
        session.SignIn(new User
        {
            UserId = 7, Username = "viewer", DisplayName = "Viewer", Role = UserRole.Viewer,
            SecurityStamp = Guid.NewGuid().ToString("N")
        }, TimeSpan.FromHours(1));
        var authorization = new AuthorizationService(session);

        Assert.True(authorization.Has(Permission.ViewCatalog));
        Assert.Throws<UnauthorizedAccessException>(() => authorization.Demand(Permission.ManageCirculation));
    }

    [Theory]
    [InlineData("short")]
    [InlineData("onlylowercase123!")]
    [InlineData("NoNumbersHere!")]
    public void PasswordPolicy_RejectsWeakPasswords(string password)
    {
        Assert.False(new PasswordPolicyService().Validate(password, "librarian").IsValid);
    }

    [Fact]
    public async Task RegisterLocalAsync_RequiresCodeBeforeCreatingSession()
    {
        await using var test = await SecurityDatabase.CreateAsync();

        var result = await test.Registration.RegisterLocalAsync(new(
            "Ada Reader", "ada@example.test", "ada.reader", "Strong@Password1", "+123456", "42 Library Lane"));

        Assert.True(result.Success);
        Assert.True(result.RequiresEmailVerification);
        Assert.False(test.Session.IsAuthenticated);
        await using var db = await test.Factory.CreateDbContextAsync();
        var user = await db.Users.Include(x => x.Member).Include(x => x.EmailVerification).SingleAsync(x => x.Username == "ada.reader");
        Assert.Equal("ada@example.test", user.Member?.Email);
        Assert.True(user.HasLocalPassword);
        Assert.False(user.IsEmailVerified);
        Assert.NotNull(user.EmailVerification);
        Assert.Equal("ada@example.test", test.Email.LastRecipient);

        var blockedLogin = await test.Authentication.LoginAsync("ada.reader", "Strong@Password1");
        Assert.False(blockedLogin.Success);
        Assert.Contains("Confirm your email", blockedLogin.Message);

        var verified = await test.Registration.VerifyEmailAsync("ada@example.test", test.Email.LastCode!);

        Assert.True(verified.Success);
        Assert.Equal(UserRole.Viewer, test.Session.Current?.Role);
        Assert.NotNull(test.Session.Current?.MemberId);
    }

    [Fact]
    public async Task RegisterLocalAsync_RejectsExistingMemberEmail()
    {
        await using var test = await SecurityDatabase.CreateAsync();
        await test.Registration.RegisterLocalAsync(new(
            "First Reader", "same@example.test", "reader.one", "Strong@Password1", null, null));

        var duplicate = await test.Registration.RegisterLocalAsync(new(
            "Second Reader", "same@example.test", "reader.two", "Another@Password2", null, null));

        Assert.False(duplicate.Success);
    }

    private sealed class SecurityDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public TestFactory Factory { get; }
        public SessionService Session { get; } = new();
        public AuthorizationService Authorization { get; }
        public AuthenticationService Authentication { get; }
        public AccountRegistrationService Registration { get; }
        public FakeEmailSender Email { get; } = new();

        private SecurityDatabase(SqliteConnection connection, TestFactory factory)
        {
            this.connection = connection;
            Factory = factory;
            Authorization = new AuthorizationService(Session);
            var audit = new AuditService(factory, Session, Authorization);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:MaxFailedLoginAttempts"] = "3",
                ["Security:LockoutMinutes"] = "10",
                ["Security:SessionHours"] = "8"
            }).Build();
            Authentication = new AuthenticationService(factory, Session, audit, new PasswordPolicyService(), configuration);
            Registration = new AccountRegistrationService(factory, new PasswordPolicyService(), Session, audit, Email,
                new GoogleOAuthService(configuration, new HttpClient()), configuration);
        }

        public static async Task<SecurityDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<LibraryDbContext>().UseSqlite(connection).Options;
            var factory = new TestFactory(options);
            var test = new SecurityDatabase(connection, factory);
            await using var db = await factory.CreateDbContextAsync();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new User
            {
                Username = "librarian", DisplayName = "Library Staff", Role = UserRole.Librarian,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Valid@Password1"),
                SecurityStamp = Guid.NewGuid().ToString("N")
            });
            await db.SaveChangesAsync();
            return test;
        }

        public async ValueTask DisposeAsync() => await connection.DisposeAsync();
    }

    private sealed class TestFactory(DbContextOptions<LibraryDbContext> options)
        : IDbContextFactory<LibraryDbContext>
    {
        public LibraryDbContext CreateDbContext() => new(options);
        public Task<LibraryDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class FakeEmailSender : IEmailSender
    {
        public string? LastRecipient { get; private set; }
        public string? LastCode { get; private set; }
        public Task SendVerificationCodeAsync(string recipientEmail, string recipientName, string code,
            TimeSpan validFor, CancellationToken cancellationToken = default)
        {
            LastRecipient = recipientEmail;
            LastCode = code;
            return Task.CompletedTask;
        }
    }
}
