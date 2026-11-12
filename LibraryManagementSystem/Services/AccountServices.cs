using Google.Apis.Auth;
using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LibraryManagementSystem.Services;

public sealed record RegistrationRequest(
    string FullName, string Email, string Username, string Password, string? Phone, string? Address);
public sealed record RegistrationResult(bool Success, string Message, bool RequiresEmailVerification = false);

public sealed record GoogleIdentityResult(
    bool Success, string Message, string? Subject = null, string? Email = null, string? DisplayName = null);

public sealed class GoogleOAuthService(IConfiguration configuration, HttpClient httpClient)
{
    private const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["GoogleOAuth:ClientId"]);

    public async Task<GoogleIdentityResult> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        var clientId = configuration["GoogleOAuth:ClientId"]?.Trim();
        if (string.IsNullOrWhiteSpace(clientId))
            return new(false, "Google sign-in is not configured. Add a Desktop OAuth client ID in appsettings.json or user secrets.");

        var verifier = Base64Url(RandomNumberGenerator.GetBytes(64));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(32));
        var nonce = Base64Url(RandomNumberGenerator.GetBytes(32));
        var timeout = TimeSpan.FromSeconds(configuration.GetValue("GoogleOAuth:TimeoutSeconds", 180));

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var redirectUri = $"http://127.0.0.1:{port}/";
            var authorizationUri = BuildAuthorizationUri(clientId, redirectUri, state, nonce, challenge);
            Process.Start(new ProcessStartInfo(authorizationUri) { UseShellExecute = true });

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            using var browser = await listener.AcceptTcpClientAsync(timeoutSource.Token);
            var callback = await ReadCallbackAsync(browser, timeoutSource.Token);
            await WriteBrowserResponseAsync(browser, callback.ContainsKey("code"), timeoutSource.Token);

            if (!callback.TryGetValue("state", out var returnedState) || !FixedTimeEquals(state, returnedState))
                return new(false, "Google sign-in was rejected because the security state did not match.");
            if (callback.TryGetValue("error", out var error))
                return new(false, error == "access_denied" ? "Google sign-in was cancelled." : $"Google sign-in failed: {error}.");
            if (!callback.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
                return new(false, "Google did not return an authorization code.");

            var form = new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = clientId,
                ["redirect_uri"] = redirectUri,
                ["grant_type"] = "authorization_code",
                ["code_verifier"] = verifier
            };
            var clientSecret = configuration["GoogleOAuth:ClientSecret"]?.Trim();
            if (!string.IsNullOrWhiteSpace(clientSecret)) form["client_secret"] = clientSecret;
            using var response = await httpClient.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form), timeoutSource.Token);
            var json = await response.Content.ReadAsStringAsync(timeoutSource.Token);
            if (!response.IsSuccessStatusCode)
                return new(false, "Google could not complete sign-in. Verify the Desktop OAuth client configuration.");

            using var tokenDocument = JsonDocument.Parse(json);
            if (!tokenDocument.RootElement.TryGetProperty("id_token", out var idTokenElement))
                return new(false, "Google did not return an identity token.");
            var idToken = idTokenElement.GetString()!;
            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken,
                new GoogleJsonWebSignature.ValidationSettings { Audience = [clientId] });
            if (!payload.EmailVerified)
                return new(false, "The Google account email address is not verified.");
            if (!TokenNonceMatches(idToken, nonce))
                return new(false, "Google sign-in was rejected because the identity nonce did not match.");
            return new(true, "Google identity verified.", payload.Subject, payload.Email,
                string.IsNullOrWhiteSpace(payload.Name) ? payload.Email : payload.Name);
        }
        catch (OperationCanceledException)
        {
            return new(false, "Google sign-in timed out or was cancelled.");
        }
        catch (Exception exception)
        {
            return new(false, $"Google sign-in could not be completed: {exception.Message}");
        }
        finally { listener.Stop(); }
    }

    private static string BuildAuthorizationUri(string clientId, string redirectUri, string state, string nonce, string challenge)
    {
        var values = new Dictionary<string, string>
        {
            ["client_id"] = clientId, ["redirect_uri"] = redirectUri, ["response_type"] = "code",
            ["scope"] = "openid email profile", ["state"] = state, ["nonce"] = nonce,
            ["code_challenge"] = challenge, ["code_challenge_method"] = "S256",
            ["access_type"] = "online", ["prompt"] = "select_account"
        };
        return AuthorizationEndpoint + "?" + string.Join("&", values.Select(x =>
            $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
    }

    private static async Task<Dictionary<string, string>> ReadCallbackAsync(TcpClient browser, CancellationToken token)
    {
        var stream = browser.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
        var requestLine = await reader.ReadLineAsync(token) ?? throw new InvalidOperationException("Empty browser callback.");
        var parts = requestLine.Split(' ');
        if (parts.Length < 2) throw new InvalidOperationException("Invalid browser callback.");
        var query = new Uri("http://127.0.0.1" + parts[1]).Query.TrimStart('?');
        return query.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Split('=', 2))
            .ToDictionary(x => Decode(x[0]), x => x.Length > 1 ? Decode(x[1]) : string.Empty);
    }

    private static async Task WriteBrowserResponseAsync(TcpClient browser, bool success, CancellationToken token)
    {
        var html = success
            ? "<html><body style='font-family:Segoe UI;padding:40px'><h2>Signed in to LibraryForge</h2><p>You can close this tab and return to the application.</p></body></html>"
            : "<html><body style='font-family:Segoe UI;padding:40px'><h2>LibraryForge sign-in was not completed</h2><p>Return to the application for details.</p></body></html>";
        var body = Encoding.UTF8.GetBytes(html);
        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await using var stream = browser.GetStream();
        await stream.WriteAsync(header, token);
        await stream.WriteAsync(body, token);
    }

    private static string Decode(string value) => Uri.UnescapeDataString(value.Replace('+', ' '));
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static bool FixedTimeEquals(string left, string right)
    {
        var a = Encoding.UTF8.GetBytes(left); var b = Encoding.UTF8.GetBytes(right);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
    private static bool TokenNonceMatches(string token, string expected)
    {
        var part = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        part = part.PadRight(part.Length + (4 - part.Length % 4) % 4, '=');
        using var document = JsonDocument.Parse(Convert.FromBase64String(part));
        return document.RootElement.TryGetProperty("nonce", out var nonce) && FixedTimeEquals(expected, nonce.GetString() ?? string.Empty);
    }
}

public sealed class AccountRegistrationService(
    IDbContextFactory<LibraryDbContext> factory,
    PasswordPolicyService passwordPolicy,
    SessionService session,
    AuditService audit,
    IEmailSender emailSender,
    GoogleOAuthService googleOAuth,
    IConfiguration configuration)
{
    private readonly TimeSpan sessionDuration = TimeSpan.FromHours(configuration.GetValue("Security:SessionHours", 8));
    private readonly TimeSpan verificationLifetime = TimeSpan.FromMinutes(configuration.GetValue("Email:VerificationCodeMinutes", 10));
    private readonly TimeSpan resendDelay = TimeSpan.FromSeconds(configuration.GetValue("Email:ResendDelaySeconds", 60));
    private readonly int maximumVerificationAttempts = configuration.GetValue("Email:MaximumVerificationAttempts", 5);
    public bool IsGoogleConfigured => googleOAuth.IsConfigured;

    public async Task<RegistrationResult> RegisterLocalAsync(RegistrationRequest request)
    {
        var fullName = request.FullName.Trim();
        var email = request.Email.Trim().ToLowerInvariant();
        var username = request.Username.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(username) || !IsEmail(email))
            return new(false, "Enter a full name, valid email address, and username.");
        var validation = passwordPolicy.Validate(request.Password, username);
        if (!validation.IsValid) return new(false, validation.Message);

        await using var db = await factory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        if (await db.Users.AnyAsync(x => x.Username == username)) return new(false, "That username is already in use.");
        if (await db.Members.AnyAsync(x => x.Email == email)) return new(false, "An account already exists for that email address.");
        var member = new Member { FullName = fullName, Email = email, Phone = request.Phone?.Trim(), Address = request.Address?.Trim() };
        var user = new User
        {
            Username = username, DisplayName = fullName, Role = UserRole.Viewer,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password), PasswordChangedAt = DateTime.UtcNow,
            Member = member, IsEmailVerified = false
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var code = GenerateVerificationCode();
        db.EmailVerifications.Add(new EmailVerification
        {
            UserId = user.UserId, CodeHash = HashVerificationCode(user, code),
            ExpiresAtUtc = DateTime.UtcNow.Add(verificationLifetime), LastSentAtUtc = DateTime.UtcNow
        });
        audit.Add(db, "MemberSelfRegistered", nameof(User), user.UserId, $"MemberId={member.MemberId}; Email={email}", user.UserId);
        await db.SaveChangesAsync();
        try
        {
            await emailSender.SendVerificationCodeAsync(email, fullName, code, verificationLifetime);
        }
        catch (Exception exception)
        {
            return new(false, $"Your account could not be created because the verification email was not sent. {exception.Message}");
        }
        await transaction.CommitAsync();
        return new(true, $"We sent a six-digit verification code to {email}.", true);
    }

    public async Task<LoginResult> VerifyEmailAsync(string email, string code, CancellationToken cancellationToken = default)
    {
        email = email.Trim().ToLowerInvariant();
        code = code.Trim();
        if (!IsEmail(email) || code.Length != 6 || !code.All(char.IsDigit))
            return new(false, "Enter the email address and six-digit verification code.");
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var user = await db.Users.Include(x => x.Member).Include(x => x.EmailVerification)
            .SingleOrDefaultAsync(x => x.Member != null && x.Member.Email == email, cancellationToken);
        if (user is null) return new(false, "No pending account was found for that email address.");
        if (user.IsEmailVerified)
        {
            session.SignIn(user, sessionDuration);
            return new(true, "This email address is already verified.");
        }
        var verification = user.EmailVerification;
        if (verification is null || verification.ExpiresAtUtc <= DateTime.UtcNow)
            return new(false, "That code has expired. Request a new verification code.");
        if (verification.AttemptCount >= maximumVerificationAttempts)
            return new(false, "Too many incorrect attempts. Request a new verification code.");
        if (!VerificationCodeMatches(user, code, verification.CodeHash))
        {
            verification.AttemptCount++;
            await db.SaveChangesAsync(cancellationToken);
            return new(false, $"That code is incorrect. {Math.Max(0, maximumVerificationAttempts - verification.AttemptCount)} attempts remain.");
        }
        user.IsEmailVerified = true;
        db.EmailVerifications.Remove(verification);
        audit.Add(db, "EmailVerified", nameof(User), user.UserId, $"Email={email}", user.UserId);
        await db.SaveChangesAsync(cancellationToken);
        session.SignIn(user, sessionDuration);
        return new(true, "Email confirmed. Welcome to LibraryForge.");
    }

    public async Task<LoginResult> ResendVerificationCodeAsync(string email, CancellationToken cancellationToken = default)
    {
        email = email.Trim().ToLowerInvariant();
        if (!IsEmail(email)) return new(false, "Enter a valid email address.");
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var user = await db.Users.Include(x => x.Member).Include(x => x.EmailVerification)
            .SingleOrDefaultAsync(x => x.Member != null && x.Member.Email == email, cancellationToken);
        if (user is null || user.IsEmailVerified) return new(false, "No unverified account was found for that email address.");
        if (user.EmailVerification is { } current && current.LastSentAtUtc.Add(resendDelay) > DateTime.UtcNow)
        {
            var seconds = (int)Math.Ceiling((current.LastSentAtUtc.Add(resendDelay) - DateTime.UtcNow).TotalSeconds);
            return new(false, $"Wait {seconds} seconds before requesting another code.");
        }
        var code = GenerateVerificationCode();
        var verification = user.EmailVerification ?? new EmailVerification { UserId = user.UserId };
        verification.CodeHash = HashVerificationCode(user, code);
        verification.ExpiresAtUtc = DateTime.UtcNow.Add(verificationLifetime);
        verification.LastSentAtUtc = DateTime.UtcNow;
        verification.AttemptCount = 0;
        if (user.EmailVerification is null) db.EmailVerifications.Add(verification);
        try
        {
            await emailSender.SendVerificationCodeAsync(email, user.DisplayName, code, verificationLifetime, cancellationToken);
        }
        catch (Exception exception)
        {
            return new(false, $"The verification email could not be sent. {exception.Message}");
        }
        audit.Add(db, "EmailVerificationResent", nameof(User), user.UserId, $"Email={email}", user.UserId);
        await db.SaveChangesAsync(cancellationToken);
        return new(true, $"A new verification code was sent to {email}.");
    }

    public async Task<LoginResult> SignInOrRegisterWithGoogleAsync(CancellationToken cancellationToken = default)
    {
        var identity = await googleOAuth.AuthenticateAsync(cancellationToken);
        if (!identity.Success) return new(false, identity.Message);
        var email = identity.Email!.Trim().ToLowerInvariant();
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var external = await db.ExternalLogins.Include(x => x.User).ThenInclude(x => x.EmailVerification)
            .SingleOrDefaultAsync(x => x.Provider == "Google" && x.ProviderSubject == identity.Subject, cancellationToken);
        User user;
        if (external is not null)
        {
            user = external.User;
            external.LastUsedAt = DateTime.UtcNow;
        }
        else
        {
            var member = await db.Members.Include(x => x.User).ThenInclude(x => x!.EmailVerification)
                .SingleOrDefaultAsync(x => x.Email == email, cancellationToken);
            if (member?.User is { } linkedUser)
            {
                user = linkedUser;
            }
            else
            {
                if (await db.Users.AnyAsync(x => x.Username == email, cancellationToken))
                    return new(false, "This email is already used by another LibraryForge account. Sign in locally first.");
                member ??= new Member { FullName = identity.DisplayName!, Email = email };
                user = new User
                {
                    Username = email, DisplayName = identity.DisplayName!, Role = UserRole.Viewer,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(Base64Url(RandomNumberGenerator.GetBytes(48))),
                    PasswordChangedAt = DateTime.UtcNow, Member = member, HasLocalPassword = false,
                    IsEmailVerified = true
                };
                db.Users.Add(user);
            }
            db.ExternalLogins.Add(new ExternalLogin
            {
                User = user, Provider = "Google", ProviderSubject = identity.Subject!, Email = email
            });
        }
        if (!user.IsActive) return new(false, "This LibraryForge account is inactive. Contact library staff.");
        user.IsEmailVerified = true;
        if (user.EmailVerification is not null) db.EmailVerifications.Remove(user.EmailVerification);
        user.LastLogin = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        audit.Add(db, external is null ? "GoogleAccountRegistered" : "GoogleLoginSucceeded", nameof(User),
            user.UserId, $"Email={email}", user.UserId);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        session.SignIn(user, sessionDuration);
        return new(true, "Signed in with Google.");
    }

    private static bool IsEmail(string email)
    {
        try { return new MailAddress(email).Address.Equals(email, StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }
    private static string GenerateVerificationCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
    private static string HashVerificationCode(User user, string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{user.SecurityStamp}:{code}")));
    private static bool VerificationCodeMatches(User user, string code, string expectedHash)
    {
        var actual = Encoding.ASCII.GetBytes(HashVerificationCode(user, code));
        var expected = Encoding.ASCII.GetBytes(expectedHash);
        return actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
    }
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed record MemberPortalSnapshot(
    Member Member, List<Loan> ActiveLoans, List<Loan> LoanHistory, List<Reservation> Reservations);

public sealed class MemberPortalService(
    IDbContextFactory<LibraryDbContext> factory,
    SessionService session,
    ReservationService reservationService,
    AuditService audit)
{
    public async Task<MemberPortalSnapshot> GetAsync()
    {
        session.EnsureActive();
        var memberId = session.Current?.MemberId ?? throw new UnauthorizedAccessException("This account is not linked to a library membership.");
        await using var db = await factory.CreateDbContextAsync();
        await reservationService.ExpireAndPromoteAsync(db);
        var member = await db.Members.AsNoTracking().SingleAsync(x => x.MemberId == memberId);
        var loans = await db.Loans.AsNoTracking().Include(x => x.Book).Where(x => x.MemberId == memberId)
            .OrderByDescending(x => x.IssuedAt).ToListAsync();
        var memberReservations = await db.Reservations.AsNoTracking().Include(x => x.Book).Where(x => x.MemberId == memberId)
            .OrderByDescending(x => x.ReservedAt).ToListAsync();
        return new(member, loans.Where(x => x.ReturnedAt is null).ToList(), loans, memberReservations);
    }

    public async Task<ServiceResult> UpdateProfileAsync(string fullName, string? phone, string? address)
    {
        session.EnsureActive();
        var memberId = session.Current?.MemberId ?? throw new UnauthorizedAccessException("This account is not linked to a library membership.");
        fullName = fullName.Trim();
        phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        address = string.IsNullOrWhiteSpace(address) ? null : address.Trim();
        if (fullName.Length is < 2 or > 120) return new(false, "Enter a name between 2 and 120 characters.");
        if (phone?.Length > 30) return new(false, "Phone number cannot exceed 30 characters.");
        if (address?.Length > 300) return new(false, "Address cannot exceed 300 characters.");
        await using var db = await factory.CreateDbContextAsync();
        var member = await db.Members.Include(x => x.User).SingleAsync(x => x.MemberId == memberId);
        member.FullName = fullName;
        member.Phone = phone;
        member.Address = address;
        if (member.User is not null) member.User.DisplayName = fullName;
        audit.Add(db, "MemberProfileUpdated", nameof(Member), memberId, "Member updated own contact profile", session.Current?.UserId);
        await db.SaveChangesAsync();
        session.UpdateDisplayName(fullName);
        return new(true, "Your profile was updated.");
    }

    public async Task<(ServiceResult Result, string? ImagePath)> UpdateProfileImageAsync(
        string sourcePath, ProfileImageStorageService storage, CancellationToken cancellationToken = default)
    {
        session.EnsureActive();
        var memberId = session.Current?.MemberId ?? throw new UnauthorizedAccessException("This account is not linked to a library membership.");
        string storedPath;
        try { storedPath = await storage.StoreAsync(sourcePath, memberId, cancellationToken); }
        catch (Exception exception) { return (new(false, exception.Message), null); }
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var member = await db.Members.SingleAsync(x => x.MemberId == memberId, cancellationToken);
        member.PhotoPath = storedPath;
        audit.Add(db, "MemberProfileImageUpdated", nameof(Member), memberId, "Member updated profile image", session.Current?.UserId);
        await db.SaveChangesAsync(cancellationToken);
        return (new(true, "Your profile image was updated."), storedPath);
    }
}
