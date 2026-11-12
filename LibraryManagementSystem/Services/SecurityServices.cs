using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LibraryManagementSystem.Services;

[Flags]
public enum Permission
{
    None = 0,
    ViewDashboard = 1 << 0,
    ViewCatalog = 1 << 1,
    ManageCatalog = 1 << 2,
    ViewMembers = 1 << 3,
    ManageMembers = 1 << 4,
    ManageCirculation = 1 << 5,
    ManageReservations = 1 << 6,
    ViewReports = 1 << 7,
    ManageUsers = 1 << 8,
    ViewAuditLog = 1 << 9,
    ManageSettings = 1 << 10
}

public sealed record AuthenticatedSession(
    int UserId,
    string Username,
    string DisplayName,
    UserRole Role,
    string SecurityStamp,
    DateTime StartedAtUtc,
    DateTime ExpiresAtUtc,
    bool MustChangePassword,
    int? MemberId,
    bool HasLocalPassword);

public sealed record LoginResult(
    bool Success,
    string Message,
    bool RequiresPasswordChange = false,
    DateTime? LockedUntilUtc = null);

public sealed record PasswordValidationResult(bool IsValid, string Message);

public sealed class SessionService
{
    public AuthenticatedSession? Current { get; private set; }
    public bool IsAuthenticated => Current is not null && !IsExpired;
    public bool IsExpired => Current is not null && Current.ExpiresAtUtc <= DateTime.UtcNow;
    public event EventHandler? SessionEnded;
    public event EventHandler? IdentityChanged;

    public void SignIn(User user, TimeSpan duration)
    {
        var now = DateTime.UtcNow;
        Current = new AuthenticatedSession(
            user.UserId, user.Username, user.DisplayName, user.Role, user.SecurityStamp,
            now, now.Add(duration), user.MustChangePassword, user.MemberId, user.HasLocalPassword);
    }

    public void Refresh(User user, TimeSpan duration) => SignIn(user, duration);

    public void UpdateDisplayName(string displayName)
    {
        if (Current is not null)
        {
            Current = Current with { DisplayName = displayName };
            IdentityChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SignOut()
    {
        Current = null;
        SessionEnded?.Invoke(this, EventArgs.Empty);
    }

    public void EnsureActive()
    {
        if (Current is null)
            throw new UnauthorizedAccessException("Sign in is required.");
        if (!IsExpired) return;
        SignOut();
        throw new UnauthorizedAccessException("Your session has expired. Sign in again.");
    }
}

public sealed class AuthorizationService(SessionService session)
{
    public Permission Permissions => session.Current?.Role switch
    {
        UserRole.Admin => Permission.ViewDashboard | Permission.ViewCatalog | Permission.ManageCatalog |
                          Permission.ViewMembers | Permission.ManageMembers | Permission.ManageCirculation |
                          Permission.ManageReservations | Permission.ViewReports | Permission.ManageUsers |
                          Permission.ViewAuditLog | Permission.ManageSettings,
        UserRole.Librarian => Permission.ViewDashboard | Permission.ViewCatalog | Permission.ManageCatalog |
                              Permission.ViewMembers | Permission.ManageMembers | Permission.ManageCirculation |
                              Permission.ManageReservations | Permission.ViewReports,
        UserRole.Viewer => Permission.ViewDashboard | Permission.ViewCatalog | Permission.ViewReports,
        _ => Permission.None
    };

    public bool Has(Permission permission) => session.IsAuthenticated && Permissions.HasFlag(permission);

    public void Demand(Permission permission)
    {
        session.EnsureActive();
        if (!Has(permission))
            throw new UnauthorizedAccessException($"Permission '{permission}' is required.");
    }
}

public sealed class PasswordPolicyService
{
    public PasswordValidationResult Validate(string password, string? username = null)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 12)
            return new(false, "Password must contain at least 12 characters.");
        if (!password.Any(char.IsUpper) || !password.Any(char.IsLower))
            return new(false, "Password must include uppercase and lowercase letters.");
        if (!password.Any(char.IsDigit) || !password.Any(ch => !char.IsLetterOrDigit(ch)))
            return new(false, "Password must include a number and a special character.");
        if (!string.IsNullOrWhiteSpace(username) && password.Contains(username, StringComparison.OrdinalIgnoreCase))
            return new(false, "Password must not contain the username.");
        return new(true, "Password meets the security policy.");
    }
}

public sealed class AuthenticationService(
    IDbContextFactory<LibraryDbContext> factory,
    SessionService session,
    AuditService audit,
    PasswordPolicyService passwordPolicy,
    IConfiguration configuration)
{
    private readonly int maxFailedAttempts = configuration.GetValue("Security:MaxFailedLoginAttempts", 5);
    private readonly TimeSpan lockoutDuration = TimeSpan.FromMinutes(configuration.GetValue("Security:LockoutMinutes", 15));
    private readonly TimeSpan sessionDuration = TimeSpan.FromHours(configuration.GetValue("Security:SessionHours", 8));
    private const string DummyHash = "$2a$11$7VSuN3PW0M9JgcN2sk4dOe3cIyZbBzVbU7pL4p1s6xD6sR7Jx7m2K";

    public async Task<LoginResult> LoginAsync(string username, string password)
    {
        var normalizedUsername = username.Trim().ToLowerInvariant();
        await using var db = await factory.CreateDbContextAsync();
        var user = await db.Users.SingleOrDefaultAsync(x => x.Username == normalizedUsername);

        if (user is null)
        {
            BCrypt.Net.BCrypt.Verify(password, DummyHash);
            audit.Add(db, "LoginFailed", nameof(User), details: $"Username={normalizedUsername}; Reason=UnknownUser");
            await db.SaveChangesAsync();
            return new(false, "Invalid username or password.");
        }

        if (!user.IsActive)
        {
            audit.Add(db, "LoginFailed", nameof(User), user.UserId, "Reason=InactiveAccount", user.UserId);
            await db.SaveChangesAsync();
            return new(false, "This account is unavailable. Contact an administrator.");
        }

        if (!user.HasLocalPassword)
        {
            audit.Add(db, "LoginFailed", nameof(User), user.UserId, "Reason=GoogleOnlyAccount", user.UserId);
            await db.SaveChangesAsync();
            return new(false, "This account uses Google sign-in. Choose Continue with Google.");
        }

        if (user.LockoutEndUtc > DateTime.UtcNow)
        {
            audit.Add(db, "LoginBlocked", nameof(User), user.UserId,
                $"LockedUntilUtc={user.LockoutEndUtc:O}", user.UserId);
            await db.SaveChangesAsync();
            return new(false, "Too many unsuccessful attempts. Try again later.", false, user.LockoutEndUtc);
        }

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            user.FailedLoginAttempts++;
            if (user.FailedLoginAttempts >= maxFailedAttempts)
            {
                user.LockoutEndUtc = DateTime.UtcNow.Add(lockoutDuration);
                user.FailedLoginAttempts = 0;
            }
            audit.Add(db, "LoginFailed", nameof(User), user.UserId,
                $"Reason=InvalidPassword; LockedUntilUtc={user.LockoutEndUtc:O}", user.UserId);
            await db.SaveChangesAsync();
            return user.LockoutEndUtc > DateTime.UtcNow
                ? new(false, "Too many unsuccessful attempts. The account is temporarily locked.", false, user.LockoutEndUtc)
                : new(false, "Invalid username or password.");
        }

        if (!user.IsEmailVerified)
        {
            audit.Add(db, "LoginFailed", nameof(User), user.UserId, "Reason=EmailNotVerified", user.UserId);
            await db.SaveChangesAsync();
            return new(false, "Confirm your email before signing in. Open Create Member Account and choose 'I already have a code'.");
        }

        user.FailedLoginAttempts = 0;
        user.LockoutEndUtc = null;
        user.LastLogin = DateTime.UtcNow;
        session.SignIn(user, sessionDuration);
        audit.Add(db, "LoginSucceeded", nameof(User), user.UserId, $"Username={user.Username}", user.UserId);
        await db.SaveChangesAsync();
        return new(true, "Signed in successfully.", user.MustChangePassword);
    }

    public async Task<ServiceResult> ChangePasswordAsync(string currentPassword, string newPassword)
    {
        session.EnsureActive();
        var principal = session.Current!;
        var validation = passwordPolicy.Validate(newPassword, principal.Username);
        if (!validation.IsValid) return new(false, validation.Message);

        await using var db = await factory.CreateDbContextAsync();
        var user = await db.Users.SingleAsync(x => x.UserId == principal.UserId);
        if (!user.HasLocalPassword) return new(false, "This account uses Google sign-in and does not have a local password.");
        if (!BCrypt.Net.BCrypt.Verify(currentPassword, user.PasswordHash))
            return new(false, "Current password is incorrect.");
        if (BCrypt.Net.BCrypt.Verify(newPassword, user.PasswordHash))
            return new(false, "The new password must be different from the current password.");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        user.PasswordChangedAt = DateTime.UtcNow;
        user.MustChangePassword = false;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        audit.Add(db, "PasswordChanged", nameof(User), user.UserId, userId: user.UserId);
        await db.SaveChangesAsync();
        session.Refresh(user, sessionDuration);
        return new(true, "Password changed successfully.");
    }

    public async Task LogoutAsync()
    {
        if (session.Current is { } principal)
            await audit.RecordAsync("Logout", nameof(User), principal.UserId, userId: principal.UserId);
        session.SignOut();
    }
}

public sealed class UserAdministrationService(
    IDbContextFactory<LibraryDbContext> factory,
    SessionService session,
    AuthorizationService authorization,
    PasswordPolicyService passwordPolicy,
    AuditService audit)
{
    public async Task<List<User>> ListAsync()
    {
        authorization.Demand(Permission.ManageUsers);
        await using var db = await factory.CreateDbContextAsync();
        return await db.Users.AsNoTracking().OrderBy(x => x.Username).ToListAsync();
    }

    public async Task CreateAsync(string username, string displayName, UserRole role, string password)
    {
        authorization.Demand(Permission.ManageUsers);
        var normalized = username.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized) || string.IsNullOrWhiteSpace(displayName))
            throw new InvalidOperationException("Username and display name are required.");
        var validation = passwordPolicy.Validate(password, normalized);
        if (!validation.IsValid) throw new InvalidOperationException(validation.Message);
        await using var db = await factory.CreateDbContextAsync();
        if (await db.Users.AnyAsync(x => x.Username == normalized))
            throw new InvalidOperationException("The username is already in use.");
        var user = new User
        {
            Username = normalized,
            DisplayName = displayName.Trim(),
            Role = role,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            PasswordChangedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        audit.Add(db, "UserCreated", nameof(User), user.UserId, $"Username={user.Username}; Role={user.Role}");
        await db.SaveChangesAsync();
    }

    public async Task ResetPasswordAsync(int id, string password)
    {
        authorization.Demand(Permission.ManageUsers);
        await using var db = await factory.CreateDbContextAsync();
        var user = await db.Users.FindAsync(id) ?? throw new InvalidOperationException("User not found.");
        var validation = passwordPolicy.Validate(password, user.Username);
        if (!validation.IsValid) throw new InvalidOperationException(validation.Message);
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
        user.PasswordChangedAt = DateTime.UtcNow;
        user.MustChangePassword = true;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.FailedLoginAttempts = 0;
        user.LockoutEndUtc = null;
        audit.Add(db, "UserPasswordReset", nameof(User), id, $"Username={user.Username}");
        await db.SaveChangesAsync();
    }

    public async Task SetActiveAsync(int id, bool active)
    {
        authorization.Demand(Permission.ManageUsers);
        if (session.Current?.UserId == id && !active)
            throw new InvalidOperationException("You cannot deactivate your own signed-in account.");
        await using var db = await factory.CreateDbContextAsync();
        var user = await db.Users.FindAsync(id) ?? throw new InvalidOperationException("User not found.");
        user.IsActive = active;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        audit.Add(db, "UserStatusChanged", nameof(User), id, $"Username={user.Username}; IsActive={active}");
        await db.SaveChangesAsync();
    }

    public async Task UnlockAsync(int id)
    {
        authorization.Demand(Permission.ManageUsers);
        await using var db = await factory.CreateDbContextAsync();
        var user = await db.Users.FindAsync(id) ?? throw new InvalidOperationException("User not found.");
        user.FailedLoginAttempts = 0;
        user.LockoutEndUtc = null;
        audit.Add(db, "UserUnlocked", nameof(User), id, $"Username={user.Username}");
        await db.SaveChangesAsync();
    }
}
