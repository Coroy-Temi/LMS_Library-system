# Google sign-in setup

LibraryForge keeps local username/password registration available offline. Google sign-in is optional and becomes active after a Google OAuth desktop client is configured.

## Configure Google Cloud

1. Create or select a Google Cloud project.
2. Configure the Google Auth Platform consent screen and add the application name, support email, and authorized test users while the app is in testing.
3. Create an OAuth client with application type **Desktop app**.
4. Copy the client ID. Copy the client secret too if Google provides one for the desktop client.

LibraryForge opens the system browser and receives the result on a temporary `127.0.0.1` loopback port. No fixed redirect URI needs to be added to `appsettings.json`.

## Configure LibraryForge locally

The safest development setup is to keep credentials out of source control and set them for the current PowerShell session:

```powershell
$env:GoogleOAuth__ClientId = "YOUR_CLIENT_ID.apps.googleusercontent.com"
$env:GoogleOAuth__ClientSecret = "YOUR_CLIENT_SECRET"
dotnet run --project ".\LibraryManagementSystem\LibraryManagementSystem.csproj"
```

`GoogleOAuth__ClientSecret` is optional in LibraryForge because a native desktop app cannot keep a distributed secret confidential. Never commit a real credential into the repository.

For a persistent local-only setup, fill the `GoogleOAuth` section of `appsettings.json` on the deployment machine and protect that file with normal Windows account permissions.

## Sign-in behavior

- New Google identities create a Viewer user linked to a new Member profile.
- A verified Google email can link to an existing member account with the same email.
- LibraryForge stores only the Google provider subject and verified email association; OAuth and ID tokens are not persisted.
- Google-only users use **Continue with Google** and do not receive a local password.
- Administrators and librarians continue to use staff accounts provisioned through the Admin panel.

## Troubleshooting

- **Google sign-in is not configured:** set `GoogleOAuth__ClientId`, then restart LibraryForge.
- **Access blocked / app not verified:** add the Google account as a test user on the consent screen.
- **Browser completes but the app times out:** allow LibraryForge through local firewall prompts so it can receive its loopback callback.
- **Redirect mismatch:** confirm the OAuth client type is Desktop app, not Web application.
