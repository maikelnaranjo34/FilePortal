# FilePortal project reference

FilePortal is a private file browser for Windows drives and UNC shares. It runs on ASP.NET Core 10 with a vanilla JavaScript frontend. Signed-in users can browse assigned folders, preview files, upload, download, and delete items. The application can run directly, under IIS, or as a Windows service.

## Running and building

Configure the folders you want to expose under `Roots` in `appsettings.json`. Install the current .NET 10 SDK before building.

```powershell
dotnet build --configuration Release
dotnet run -- --provision-admin your-admin-name
dotnet run
```

Run the provisioning command only when the user store has no administrator. It prompts for a password and confirmation. Open the URL printed by the host. Swagger is available in Development.

## Project structure

| Path | Responsibility |
| --- | --- |
| [Program.cs](Program.cs) | Startup, local administrator provisioning, authentication cookie validation, and routing |
| [Controllers/AuthController.cs](Controllers/AuthController.cs) | Sign-in, sign-out, account details, and optional login notifications |
| [Controllers/FilesController.cs](Controllers/FilesController.cs) | Folder listings, previews, file downloads, uploads, deletion, and ZIP archives |
| [Controllers/UsersController.cs](Controllers/UsersController.cs) | User administration, folder assignment, and password changes |
| [Services/FileBrowserService.cs](Services/FileBrowserService.cs) | Configured roots and filesystem authorization shared by file endpoints |
| [Services/UserStoreService.cs](Services/UserStoreService.cs) | JSON persistence, password hashing, and atomic updates |
| [Services/ThumbnailService.cs](Services/ThumbnailService.cs) | SkiaSharp thumbnail generation and a bounded in-memory cache |
| [wwwroot/index.html](wwwroot/index.html) | File browser and sign-in |
| [wwwroot/users.html](wwwroot/users.html) | Account settings and administrator controls |
| [tests/fileportal.test.mjs](tests/fileportal.test.mjs) | Integration tests against the published application |

## Authentication and folder access

- The local provisioning command replaces browser-based administrator setup. Sign-in uses existing accounts in the JSON store. Passwords use salted PBKDF2-SHA256 hashes.
- The authentication cookie has a seven-day sliding expiration. The app checks the account and its security stamp on each authenticated request. Password changes, role changes, and account removal invalidate affected sessions.
- Administrators can operate within configured roots. Regular users can operate within assigned folders under those roots. An empty assignment grants no file access. Changes to folder assignments take effect on subsequent requests.
- All file operations use the shared path policy. Application files, user data, filesystem links, and Windows short-path aliases are excluded. Uploads keep relative folder structure within the selected destination, and root directories are protected from deletion.
- The hosting identity also needs the appropriate filesystem permissions and, for a UNC path, share permissions. Unavailable roots do not appear in the browser.

The data directory defaults to `data` beside the application assembly. Set `FilePortal__DataDirectory` to use a different location outside `wwwroot`. It holds `users.json`, data protection keys, and temporary ZIP files. Update operations are serialized and replace the user store atomically. A malformed user store produces an error and remains available for recovery.

## API endpoints

### Authentication

| Method | Route | Access |
| --- | --- | --- |
| POST | `/api/auth/login` | Public. Send form fields `username` and `password`. |
| POST | `/api/auth/logout` | Signed-in user |
| GET | `/api/auth/me` | Signed-in user. Returns `user`, `isAdmin`, and `allowedFolders`. |

### Files

File endpoints require sign-in and enforce the caller's folder access.

| Method | Route | Behavior |
| --- | --- | --- |
| GET | `/api/files/roots` | Returns configured roots for administrators and available assigned folders for regular users. |
| GET | `/api/files/folder`path=` | Lists folder contents. |
| GET | `/api/files/browse`path=` | Lists subfolders for the administrator folder picker. |
| GET | `/api/files/category`path=&category=&maxResults=` | Searches recursively for pictures, videos, or documents. The default result limit is 500. |
| GET | `/api/files/thumbnail`path=` | Returns a JPEG thumbnail or a file-type icon. |
| GET | `/api/files/file`path=` | Serves a file inline with byte-range support. |
| GET | `/api/files/open`path=` | Downloads a single file as an attachment. |
| POST | `/api/files/upload`targetPath=` | Accepts multipart fields named `files`. The app's limit is 10 GB, subject to hosting limits. |
| DELETE | `/api/files/delete`path=` | Removes a file or recursively removes a directory. |
| POST | `/api/files/download-zip` | Accepts a JSON object with a `paths` array. |
| POST | `/api/files/download-zip-form` | Accepts repeated `paths` form fields for a native browser download. |

### Users

| Method | Route | Access |
| --- | --- | --- |
| GET | `/api/users` | Administrator. Lists users. |
| POST | `/api/users` | Administrator. Creates an account from form fields. |
| DELETE | `/api/users/{username}` | Administrator. The signed-in account and the last administrator are protected. |
| POST | `/api/users/{username}/promote` | Administrator. Grants the administrator role. |
| POST | `/api/users/{username}/demote` | Administrator. Self-demotion and demotion of the last administrator are blocked. |
| POST | `/api/users/{username}/password` | Users change their own password with `currentPassword` and `newPassword` form fields. Administrators can reset another account with `newPassword`. |
| GET | `/api/users/{username}/folders` | Administrator. Reads an account's assigned folders. |
| POST | `/api/users/{username}/folders` | Administrator. Replaces folder assignments with a JSON array of paths. |

## Frontend and media

`index.html` is the main portal. The former `portal.html` and `login.html` URLs redirect to it. The UI checks `/api/auth/me` on load and clears the file listing after sign-out. Letter keys cycle through matching items. API responses use camelCase.

Videos support byte-range responses for seeking. ZIP archives are prepared in the protected data directory, then downloaded by the browser and removed when the response closes. Thumbnails for JPEG, PNG, GIF, BMP, and WebP are generated with SkiaSharp at up to 240 pixels per side and cached in memory.

## Deployment and upgrade

Publish using `dotnet publish FilePortal.csproj -c Release`. Preserve the existing `users.json` or point the new deployment at its current data directory. Existing password hashes and folder assignments remain compatible. Review assignments against the configured roots and have users sign in again.

The IIS setup script uses published output. It preserves the site's existing bindings and gives new sites a loopback binding on port 5251. The service setup script uses Local Service and defaults to loopback. Configure an HTTPS endpoint for network access. For IIS startup failures after a runtime update, check the hosting bundle, app pool identity, and access to the published app before adjusting permissions. [IIS guide](IIS-DEPLOYMENT-README.md), [Windows service guide](SERVICE-README.md).

Login notifications are optional. Provide the SMTP credentials and sender and recipient addresses through the hosting environment or a secret manager. Retired credentials remain in Git history and must be revoked or rotated at their source. [Secrets guide](SECRETS.md).

## Verification

```powershell
dotnet build Fileportal.sln
node --test tests/fileportal.test.mjs
dotnet list FilePortal.csproj package --vulnerable --include-transitive
```

The Windows build and Release integration suite passed. The suite covered legacy users, concurrent account updates, permission enforcement, session revocation, password changes, uploads, ZIP downloads, thumbnails, and video byte ranges. Chrome verified sign-in, native folder ZIP downloads, password changes, and logout. The NuGet audit reported no vulnerable packages. The integration suite also runs in [GitHub Actions](.github/workflows/test.yml).

The .NET 10 release is deployed to IIS with Hosting Bundle 10.0.15. Live checks confirmed localhost HTTPS against the bound certificate, administrator sign-in, six available roots, and delivery of a login notification to the configured Gmail mailbox. The migration preserved three accounts and reset the account that used the retired password. The replacement SMTP credential is configured through the IIS application pool environment.

HTTPS remains limited to localhost by request. The existing LAN binding uses HTTP. This deployment has no configured UNC roots.
