# LibraryForge

LibraryForge is an offline-first library management system for Windows, built with WPF and .NET 8. It combines day-to-day catalog, membership, circulation, reservation, reporting, and administration workflows with a modern light/dark interface and installation-bound encrypted QR labels for physical inventory.



## Highlights

- Book catalog and inventory management with search, cover images, CSV import, and Excel export
- Accession-level physical copy tracking with condition, maintenance, loss, restoration, retirement, and event history
- Member registration, email confirmation, profile editing, profile images, expiry, and borrowing history
- Exact-copy issue and return workflows with reservation protection and automatic overdue fines
- FIFO reservation waitlists with pickup windows and automatic promotion
- Role-based access for administrators, librarians, and viewers
- Local BCrypt authentication, lockout controls, password policy, and optional Google sign-in
- Dashboard statistics, overdue PDF reports, inventory workbooks, audit logging, and SQLite backup/restore
- Light and dark Material Design themes using the LibraryForge teal and amber palette
- Internal encrypted QR labels generated without a hosted QR service

## Technology

- .NET 8 WPF
- CommunityToolkit.Mvvm
- Microsoft.Extensions.Hosting dependency injection
- Entity Framework Core 8 with SQLite
- MaterialDesignInXamlToolkit
- BCrypt.Net-Next
- QRCoder
- ClosedXML and QuestPDF
- Serilog
- xUnit

## Solution structure

```text
LibraryManagementSystem/
├── Data/          EF Core context and database initialization
├── Docs/          Architecture and integration guides
├── Models/        Domain entities and enums
├── Resources/     Application branding assets
├── Services/      Business, security, reporting, and infrastructure services
├── Themes/        Shared light/dark UI resources
├── ViewModels/    MVVM presentation logic
└── Views/         WPF windows and user controls

LibraryManagementSystem.Tests/
└── Unit and integration tests
```

The living architecture document is available at [LibraryForge_Architecture.docx](LibraryManagementSystem/Docs/LibraryForge_Architecture.docx).

## Requirements

- Windows 10 or Windows 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) for development
- Visual Studio 2022 with the **.NET desktop development** workload, or another .NET-compatible editor

## Build and run

From the repository root:

```powershell
dotnet restore .\LibraryManagementSystem.slnx
dotnet build .\LibraryManagementSystem.slnx -c Release
dotnet run --project .\LibraryManagementSystem\LibraryManagementSystem.csproj
```

The application creates its SQLite database on first launch and seeds sample catalog data plus initial staff accounts for local development. Change seeded passwords immediately before using the application with real library data.

## Run tests

```powershell
dotnet test .\LibraryManagementSystem.slnx -c Release
```

## Configuration

The checked-in `appsettings.json` contains safe defaults and empty placeholders. Do not commit real SMTP passwords, OAuth secrets, production database files, or user data.

For local email verification, copy the relevant settings into an ignored `appsettings.Local.json` or use another secure configuration provider. See [EmailVerificationSetup.md](LibraryManagementSystem/Docs/EmailVerificationSetup.md).

Google sign-in is optional. Setup guidance is in [GoogleSignInSetup.md](LibraryManagementSystem/Docs/GoogleSignInSetup.md).

## Internal QR security

LibraryForge renders QR images locally. It does not send QR contents to an external QR-generation or decoding service.

Each label contains an `LF1` envelope encrypted and authenticated with AES-256-GCM. The installation key is random and protected for the current Windows user with Windows Data Protection API. Modified labels and labels created by another LibraryForge installation are rejected.

A standard scanner can still extract the encrypted `LF1...` text because the image uses the normal QR standard, but the protected payload is not readable or forgeable without the installation key.

The installation key is deliberately not stored in this repository. Database backups and QR-key recovery must be handled as separate protected operations.

## Data and privacy

The repository excludes:

- SQLite database, WAL, and shared-memory files
- logs and generated reports
- build and IDE state
- local configuration overrides and environment files
- profile images and per-user preferences
- Windows-protected QR installation keys
- architecture rendering intermediates

Review staged changes before every release to ensure that no operational library data or credentials are included.

## Publish for Windows

```powershell
dotnet publish .\LibraryManagementSystem\LibraryManagementSystem.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true
```

The published application will be under `LibraryManagementSystem\bin\Release\net8.0-windows\win-x64\publish`.

## Status

Implemented functionality includes authentication, email verification, optional Google identity, RBAC, catalog and member management, reservations, accession inventory, encrypted internal QR labels, exact-copy circulation, reporting, audit history, backups, profile settings, and theme switching.

Planned extensions include printable multi-label sheets, member-card QR support, authorized QR-key recovery, configurable lending policies, notifications, recommendations, fuzzy search, metadata lookup, and kiosk scanning.
