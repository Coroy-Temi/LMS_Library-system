# Registration email verification setup

Local member registration requires an SMTP account so LibraryForge can deliver six-digit confirmation codes. Google registration does not use this SMTP flow because Google already supplies a verified email identity.

## Recommended configuration

Keep SMTP credentials outside source control. Set them in PowerShell before starting LibraryForge:

```powershell
$env:Email__SmtpHost = "smtp.your-provider.example"
$env:Email__SmtpPort = "587"
$env:Email__EnableSsl = "true"
$env:Email__Username = "smtp-account"
$env:Email__Password = "smtp-password-or-app-password"
$env:Email__SenderEmail = "noreply@your-library.example"
$env:Email__SenderName = "LibraryForge"

dotnet run --project ".\LibraryManagementSystem\LibraryManagementSystem.csproj"
```

Use the host, port, credentials, and TLS requirements supplied by the library's email provider. The sender address must normally be authorized by that provider.

## Verification policy

The defaults in `appsettings.json` are:

- Code lifetime: 10 minutes
- Resend delay: 60 seconds
- Maximum incorrect attempts: 5
- Code format: six digits
- Stored database value: SHA-256 hash, never the plaintext code

These values can be overridden with `Email__VerificationCodeMinutes`, `Email__ResendDelaySeconds`, and `Email__MaximumVerificationAttempts`.

## User flow

1. The member completes local registration.
2. LibraryForge creates an unverified Viewer account and sends a code.
3. The member enters the code in the registration window.
4. LibraryForge verifies the code and starts the member session.
5. If the window was closed, the member can reopen registration and choose **I already have a verification code**.

If SMTP delivery fails, the database transaction is rolled back and the account is not left half-created.
