# FilePortal

FilePortal is an ASP.NET Core 10 photo gallery and file browser for Windows drives and UNC shares. The landing page provides Photos, Albums, Favorites, and Search. File Manager provides previews, uploads, deletion, and ZIP downloads.

## Run locally

1. Install the current .NET 10 SDK.
2. Set the paths under `Roots` in `appsettings.json` to folders you want the portal to expose. Use dedicated shared directories when possible.
3. If the user store has no administrator, provision one locally. The command prompts for the password and confirmation.

   ```powershell
   dotnet run -- --provision-admin your-admin-name
   ```

4. Start the application and open the URL printed by the host.

   ```powershell
   dotnet run
   ```

Use **Manage Users** to add accounts and assign folders. Administrators can manage files within configured roots. Regular users can work within their assigned folders inside those roots. Application directories, the user store, and filesystem links are excluded. **My Account** provides self-service password changes.

## Configuration and upgrades

| Setting | Purpose |
| --- | --- |
| `Roots` | Maps a display name to a fully qualified directory. UNC shares must be reachable by the account running FilePortal. |
| `FilePortal__DataDirectory` | Optional absolute path for users, authentication keys, and temporary ZIP files. Defaults to `data` beside the application assembly. |
| `Email__SmtpUsername` and `Email__SmtpPassword` | Optional SMTP credentials managed outside the repository. Configure `Email__FromEmail` and `Email__ToEmail` to enable delivery. |

If you're upgrading from .NET 8, point the new deployment at the existing data directory or copy `users.json` into the new data directory before starting it. Existing user passwords and folder assignments remain compatible. Review existing assignments against the new deployment's configured roots. Users must sign in again after upgrading.

Rotate any administrator or SMTP credential previously used by the project or published in repository history. See [Secrets](SECRETS.md), [IIS deployment](IIS-DEPLOYMENT-README.md), and [Windows service deployment](SERVICE-README.md).

## Verification

Run the Release integration suite with Node.js 22 or later and the .NET 10 SDK.

```powershell
node --test tests/fileportal.test.mjs
dotnet list FilePortal.csproj package --vulnerable --include-transitive
```
