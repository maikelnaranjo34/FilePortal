# FilePortal

FilePortal is a photo and video gallery and file browser for Windows drives and UNC shares. It runs on ASP.NET Core 10 with a vanilla JavaScript frontend. Signed-in users can browse assigned folders, preview files, upload, download, and delete items. The application can run directly, under IIS, or as a Windows service.

- [Run locally](#run-locally)
- [FileNest redesign status](#filenest-redesign-status)
- [Project structure](#project-structure)
- [Authentication and folder access](#authentication-and-folder-access)
- [API endpoints](#api-endpoints)
- [Frontend and media](#frontend-and-media)
- [Configuration](#configuration)
- [Deployment and upgrades](#deployment-and-upgrades)
- [Verification](#verification)
- [Deployment history](#deployment-history)

## FileNest redesign status

The redesign is developed on the isolated `redesign/filenest` branch under `F:\My_Apps\FilePortal\redesign`. The active IIS site and its user data remain unchanged until the redesign is reviewed, merged, and deployed.

| Phase | Status | Scope |
| --- | --- | --- |
| 1. Project audit | Complete | ASP.NET startup, controllers, storage, authentication, frontend behavior, tests, and four supplied visual references were reviewed before implementation. |
| 2. Plan and protection | Complete | Work started from clean commit `0ca7fc2` in an isolated branch with disposable preview data and rollback through Git. |
| 3. Visual foundation | Complete | Shared FileNest tokens, glass panels, generated mascot, top navigation, menus, dialogs, responsive breakpoints, transitions, light and dark themes, and touch targets are in place. Right-click is temporarily blocked with an accessible FileNest status message. |
| 4. Page redesign | In progress | Dashboard, My Files, Gallery, Shared, Favorites, Trash status, file previews, Settings, login, and public shares have initial FileNest layouts. |
| 5. Functional integration | In progress | Existing APIs power authentication, browsing, uploads, downloads, deletion, media, sharing, and account management. Global file search remains to be completed. |
| 6. Testing and validation | In progress | Release integration tests pass. Reproducible browser validation will be added to the repository before this phase is complete. |
| 7. Mobile layout | In progress | The top-only mobile navigation and responsive dashboard are implemented. Final mobile regression results will be recorded after the preceding phases close. |

## Run locally

1. Install the .NET 10 SDK and set the folders you want to expose under `Roots` in `appsettings.json`. Prefer dedicated shared directories where possible.
2. If the user store has no administrator, provision one locally. FilePortal prompts for a password and confirmation.

   ```powershell
   dotnet run -- --provision-admin your-admin-name
   ```

3. Start FilePortal and open the URL printed by the host. Swagger is available in Development.

   ```powershell
   dotnet run
   ```

## Project structure

| Path | Responsibility |
| --- | --- |
| [Program.cs](Program.cs) | Startup, local administrator provisioning, authentication cookie validation, and routing |
| [Controllers/AuthController.cs](Controllers/AuthController.cs) | Sign-in, sign-out, account details, and optional login notifications |
| [Controllers/FilesController.cs](Controllers/FilesController.cs) | Folder listings, previews, file downloads, uploads, deletion, and ZIP archives |
| [Controllers/PhotosController.cs](Controllers/PhotosController.cs) | Photo timeline, favorites, previews, and library scans |
| [Controllers/AlbumsController.cs](Controllers/AlbumsController.cs) | Album creation and editing |
| [Controllers/SearchController.cs](Controllers/SearchController.cs) | Photo search and camera filters |
| [Controllers/SharingController.cs](Controllers/SharingController.cs) | Public photo and album links checked against the owner's current folder access |
| [Controllers/UsersController.cs](Controllers/UsersController.cs) | User administration, folder assignment, and password changes |
| [Services/FileBrowserService.cs](Services/FileBrowserService.cs) | Configured roots and filesystem authorization shared by file endpoints |
| [Services/UserStoreService.cs](Services/UserStoreService.cs) | JSON persistence, password hashing, and atomic updates |
| [Services/ThumbnailService.cs](Services/ThumbnailService.cs) | SkiaSharp image thumbnails, FFmpeg video stills, and a bounded in-memory cache |
| [wwwroot/index.html](wwwroot/index.html), [wwwroot/filenest/dashboard.js](wwwroot/filenest/dashboard.js) | FileNest dashboard, recent activity, quick access, and share management |
| [wwwroot/filenest/site.css](wwwroot/filenest/site.css), [wwwroot/filenest/workspace.css](wwwroot/filenest/workspace.css), [wwwroot/filenest/shell.js](wwwroot/filenest/shell.js) | Shared theme and responsive navigation |
| [wwwroot/gallery.html](wwwroot/gallery.html), [wwwroot/app.js](wwwroot/app.js) | Media Gallery with Photos, Videos, Albums, Favorites, and Search |
| [wwwroot/files.html](wwwroot/files.html) | File Manager |
| [wwwroot/login.html](wwwroot/login.html) | Gallery sign-in |
| [wwwroot/users.html](wwwroot/users.html) | Account settings and administrator controls |
| [tests/fileportal.test.mjs](tests/fileportal.test.mjs) | Integration tests against the published application |

Route file access through `FileBrowserService` and user records through `UserStoreService`. Login and cookie validation use the account's current security stamp.

## Authentication and folder access

Sign in as an administrator and open **Manage Users** to add an account. Select **Folders** beside a regular user, browse the configured roots, and choose **Add** for each folder. Choose **Save** to apply the assignment. To revoke all file access, remove the assigned folders and save. Users can change their password under **My Account**.

- Sign-in uses provisioned accounts in the JSON store. Passwords use salted PBKDF2-SHA256 hashes.
- The authentication cookie has a seven-day sliding expiration. The app checks the account and its security stamp on each authenticated request. Password changes, role changes, and account removal invalidate affected sessions.
- Administrators can operate within configured roots. Regular users can operate within assigned folders under those roots. An empty assignment grants no file access. Changes to folder assignments take effect on subsequent requests.
- All file operations use the shared path policy. Application files, user data, filesystem links, and Windows short-path aliases are excluded. Uploads keep relative folder structure within the selected destination, and root directories are protected from deletion.
- The hosting identity also needs the appropriate filesystem permissions and, for a UNC path, share permissions. Unavailable roots do not appear in the browser.

Each account in `users.json` stores fully qualified folder paths in `AllowedFolders` and a `SecurityStamp` for session validation. Accounts from older stores can still sign in. Updates are serialized and replace the user store atomically. A malformed user store produces an error and remains available for recovery.

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
| GET | `/api/files/folder?path=` | Lists folder contents. |
| GET | `/api/files/browse?path=` | Lists subfolders for the administrator folder picker. |
| GET | `/api/files/category?path=&category=&maxResults=` | Searches recursively for pictures, videos, or documents. The default result limit is 500. |
| GET | `/api/files/thumbnail?path=` | Returns a JPEG thumbnail or a file-type icon. |
| GET | `/api/files/file?path=` | Serves a file inline with byte-range support. |
| GET | `/api/files/open?path=` | Downloads a single file as an attachment. |
| POST | `/api/files/upload?targetPath=` | Accepts multipart fields named `files`. The app's limit is 10 GB, subject to hosting limits. |
| DELETE | `/api/files/delete?path=` | Removes a file or recursively removes a directory. |
| POST | `/api/files/batch/delete` | Accepts a JSON `paths` array. Checks access for the full selection before deleting and returns successful paths and failures. |
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

### Gallery

| Method | Route | Behavior |
| --- | --- | --- |
| GET | `/api/photos/timeline` | Returns a paginated timeline. Set `videosOnly=true` or `favorites=true` to filter it. |
| GET | `/api/photos/{id}/thumbnail` and `/api/photos/{id}/file` | Serve a thumbnail or the original media. |
| POST | `/api/photos/batch/delete` | Administrator. Deletes a selection by media ID and reports failures. |
| POST | `/api/photos/batch/download-form` | Downloads selected media as a ZIP through the browser. |
| POST | `/api/photos/scan` | Administrator. Requests a library scan. |
| GET | `/api/albums` and `/api/search` | List albums or search accessible media. |
| GET | `/api/sharing/public/{token}` | Opens a public share subject to the owner's current folder access. |

## Frontend and media

The FileNest dashboard opens at `/`. It shows authorized roots, media favorites, and activity recorded by this browser for the signed-in account. Quick Access can pin available roots. The folder storage check reports the count and combined size of files directly in the selected folder. File Manager searches the current listing by name, and Gallery searches indexed photos and videos. File Manager is available at `/files.html`, and Gallery at `/gallery.html`. Older gallery bookmarks such as `/#videos` still open the corresponding view. Existing public media links continue to use `/share.html`.

On phones, the sticky top navigation shows Home, My Files, Gallery, and More. More opens Shared, Favorites, Trash, and Settings. The upload dialog uses an authorized root as its destination. For a nested destination, open that folder in My Files and upload there. Shared lists outgoing photo and album links and lets owners revoke them. Deletion is permanent in the current backend, so Trash explains the limitation and disables recovery.

The assistant opens in its own dialog. It links users to existing file search while AI is unavailable. A future AI integration should use an authenticated server endpoint, authorize each requested file through the existing folder policy, keep provider credentials server-side, and obtain explicit authorization before sending private file contents to an external service. [Assistant module](wwwroot/filenest/assistant.js).

The gallery uses the same folder access policy as File Manager. Its metadata cache, albums, favorites, and share records are stored in the application data directory. Set `PhotoLibrary__ScanRoots__0` and subsequent entries to narrow the scanned folders to paths within configured roots. Automatic scans use all configured roots and skip system and program installation directories such as `Program Files`. Explicit scan roots can include media folders that automatic scans would skip.

On Android and other narrow screens, File Manager uses a compact list and a **Drives** toggle. Tap **Delete** beside a file for individual deletion. For several items, tap **Select**, choose the items, and tap **Delete selected**. To select consecutive items, tap **Range** and then tap the first and last visible items. On desktop, Shift-click the last item after selecting the first. File Manager also has **Select all**. Deleting a folder removes its contents.

In Photos, administrators can delete an item from its preview or use **Select**, **Range**, and **Delete selected**. A preview or download opened through FilePortal allows authorized deletion. If Windows denies the operation, the app shows whether the item is read-only, open elsewhere, or blocked by folder permissions. Image and video previews provide clockwise and counterclockwise rotation. Rotation applies to the preview and leaves the stored media intact.

Photos and Favorites refresh when you return to the browser tab. Deleted files are omitted from gallery results even before the next library scan.

Browsers revalidate HTML, JavaScript, and CSS before reusing them. Touch devices also use the compact layout at wider phone viewport sizes.

Videos support byte-range responses for seeking. With FFmpeg configured, Videos, Photos, File Manager, and shared galleries show a thumbnail from about two seconds into each clip. Short clips use their opening frame. The grid loads still images, and tapping a video opens the player. Clips that cannot be decoded display the video icon.

ZIP archives are prepared in the protected data directory, then downloaded by the browser and removed when the response closes. SkiaSharp image thumbnails and FFmpeg video thumbnails fit within 240 pixels per side and share a bounded in-memory cache.

## Configuration

| Setting | Purpose |
| --- | --- |
| `Roots` | Maps display names to fully qualified local or UNC directories. Use `Roots__Name` for an environment variable. |
| `FilePortal__DataDirectory` | Sets an absolute path outside `wwwroot`. Defaults to `data` beside the application assembly. |
| `PhotoLibrary__ScanRoots__0` and subsequent entries | Restrict gallery scans to specific folders within configured roots. |
| `VideoThumbnails__FfmpegPath` | Sets the absolute path to the FFmpeg 8 executable. In JSON, use `FfmpegPath` under `VideoThumbnails`. |
| `Email__SmtpUsername` and `Email__SmtpPassword` | Optional SMTP credentials. For Gmail with an app password, use the Gmail address as the username and set the app password as `Email__SmtpPassword`. It is separate from your FilePortal sign-in password. |
| `Email__FromEmail` and `Email__ToEmail` | Sender and recipient for optional sign-in notifications. |
| `Email__SmtpHost`, `Email__SmtpPort`, and `Email__EnableSsl` | SMTP server, port, and TLS. The checked-in settings use `smtp.gmail.com`, port `587`, and TLS enabled. |

Protect and back up the data directory. It holds `users.json`, Data Protection keys, gallery metadata, albums, favorites, and share records. It also holds temporary ZIP files, so allow disk space for archives and uploads.

Store SMTP credentials in the hosting environment or a secret manager. Revoke credentials exposed in Git history at their source and replace any reused passwords.

To enable video thumbnails, give the hosting identity read and execute access to the configured FFmpeg executable and restart the app. If the setting is omitted, video tiles use the video icon.

## Deployment and upgrades

Use the same protected data directory when upgrading an existing deployment. To migrate accounts to a new store, copy `users.json` before starting the app. Existing password hashes and assignments from .NET 8 deployments remain compatible. Review the assigned folders against the configured roots and have users sign in again after the upgrade.

For a new store, run the provisioning command from the publish directory with the same data configuration the host will use.

```powershell
dotnet FilePortal.dll --provision-admin your-admin-name
```

### IIS

Install IIS and the .NET 10 Hosting Bundle. Publish the application, then run [the IIS setup script](setup-iis.ps1) from an elevated PowerShell session.

```powershell
dotnet publish FilePortal.csproj -c Release
.\setup-iis.ps1
```

The default publish directory is `bin\Release\net10.0\publish`. Use `-PublishPath` for a different release directory. The script preserves the bindings of an existing FilePortal site and uses `127.0.0.1:5251` for a new site. For network HTTPS, configure an IIS binding and certificate.

Give the app pool identity read and execute access to the published application, modify access to the data directory, and the access required by each configured root. UNC paths require both share and NTFS permissions for the identity that accesses the share. Review any broad `IIS_IUSRS` grant left by an older deployment.

Run [the IIS inspection script](check-iis-setup.ps1) to check the site, bindings, application pool, and publish files. For startup failures, check the Hosting Bundle and app pool access. If needed, temporarily enable ANCM stdout logging in [web.config](web.config) and grant the app pool write access to the log directory. IIS request filtering sets its own upload size limit.

### Windows service

Install the ASP.NET Core 10 runtime. Publish the Windows executable, then run [the service install script](install-service.ps1) from an elevated PowerShell session.

```powershell
dotnet publish FilePortal.csproj -c Release -r win-x64 --self-contained false
.\install-service.ps1 -Start
```

The script uses the bundled NSSM, configures the application's working directory, and listens on `http://127.0.0.1:5251` by default. Use `-PublishPath` for a different release directory and `-Urls` to set the listener. Stop an existing FilePortal service before reconfiguring it. The service runs as Local Service. Grant `NT SERVICE\FilePortal` the necessary rights on configured local roots and any custom data directory. Use an HTTPS reverse proxy or HTTPS endpoint for network access. Service logs are written to `%ProgramData%\FilePortal\logs`.

## Verification

Use the .NET 10 SDK and Node.js 22 or later.

```powershell
dotnet build Fileportal.sln
node --test tests/fileportal.test.mjs
dotnet list FilePortal.csproj package --vulnerable --include-transitive
```

The integration suite publishes to a temporary directory and runs on loopback against disposable accounts and files. It covers legacy user data, concurrent account updates, authorization, session revocation, password changes, uploads, ZIP downloads, image thumbnails, and video byte ranges. With FFmpeg 8 on the test runner's PATH, it also checks frame selection, short clips, and JPEG thumbnails in the gallery, File Manager, and share links. [GitHub Actions workflow](.github/workflows/test.yml).

Before deploying a redesigned build, test responsive navigation at 320–430 CSS pixels and in landscape, plus upload, download, file and video previews, deep links, and session handling with administrator and regular accounts. Test against disposable roots and app data.

After deployment, verify the expected bindings, sign-in, access to each configured root and UNC share, uploads of representative size, and optional SMTP delivery. With video thumbnails enabled, open **Videos** and check that a thumbnail renders and the clip opens when tapped.

## Deployment history

- The .NET 10 migration kept the existing user store. IIS serves the LAN over HTTP and uses HTTPS on localhost. The SMTP replacement credential is configured through the IIS application pool environment.
- The gallery rollout restored signed-in navigation on the LAN, reused the active media index, and kept File Manager at `/files.html`. Follow-up releases added Android layout changes, single and batch deletion, first-to-last range selection, preview rotation, and refresh after files are deleted outside FilePortal.
- On November 18, 2026, the video thumbnail release was deployed to IIS with FFmpeg 8.1.1. Post-deployment checks confirmed the Videos link and sign-in requirement on both bindings and the application pool's access to FFmpeg. Chrome's Android emulation verified rendering a thumbnail and tapping it to open the video on the published build.
