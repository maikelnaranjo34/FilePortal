# FilePortal

FilePortal is a photo and video gallery and file browser for Windows drives and UNC shares. It runs on ASP.NET Core 10 with a vanilla JavaScript frontend. Signed-in users can browse assigned folders, preview files, upload, download, and delete items. The application can run directly, under IIS, or as a Windows service.

- [Run locally](#run-locally)
- [FileNest redesign status](#filenest-redesign-status)
- [AI assistant roadmap](#ai-assistant-roadmap)
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
| 4. Page redesign | Complete | Dashboard, My Files, Gallery, Shared, Favorites, Trash status, previews, Settings, sign-in, and public shares use the FileNest design. Every page has a Back control with a Home fallback. Keyboard activation, dialog focus, accessible labels, and explicit unsupported-state messaging were added. |
| 5. Functional integration | Complete | Authentication, authorization, cross-root search, browsing, upload, download, deletion, previews, media, sharing, and account management use real APIs. The AI panel remains isolated and accurately disabled except for file-name search. |
| 6. Testing and validation | Complete | Release integration tests pass. Reproducible Chrome browser validation is available in `tests/filenest.browser.mjs`; Windows runners execute it in GitHub Actions. |
| 7. Mobile layout | Complete | The top-only mobile navigation and responsive dashboard are implemented. The browser regression checks 320, 360, 390, and 430 px layouts plus desktop. |
| 8. AI and voice assistant | In progress | Add secure administrator-managed OpenAI and AssemblyAI configuration, an authorization-aware OpenAI assistant, AssemblyAI listening and speech, content-based duplicate discovery, and the animation-ready Nesti mascot. |

## AI assistant roadmap

The FileNest assistant will use a cascaded provider design: AssemblyAI converts microphone audio to text, OpenAI reasons and invokes FilePortal tools, and AssemblyAI converts the final response to speech. Provider credentials and privileged file operations remain on the server. The browser receives only short-lived AssemblyAI session tokens and non-secret configuration such as model and voice identifiers.

Implementation is divided into independently testable phases:

1. **Secure provider configuration — complete in source.** Settings now has an administrator-only **AI & Voice** section. OpenAI and AssemblyAI keys are encrypted at rest with ASP.NET Core Data Protection backed by the machine-protected FilePortal key ring, never returned to the browser, and can be replaced or removed. Administrators can test each saved key, refresh OpenAI models available to that key, and save the enabled state plus OpenAI, AssemblyAI STT, AssemblyAI TTS, voice, and language defaults. The protected `ai-settings.json` store lives in the FilePortal data directory outside `wwwroot`.
2. **OpenAI assistant foundation — complete in source.** The authenticated conversation endpoint uses the selected OpenAI model with bounded client history, server-side instructions, request cancellation, provider timeouts, and safe error responses. The dashboard dialog detects availability and supports an accessible text conversation without exposing the provider key. OpenAI can call the server-defined `search_files` tool; its query is executed by the same recursive search service as My Files and its scope is derived exclusively from the signed-in user's current folder permissions. The model cannot supply or expand the search roots.
3. **AssemblyAI voice pipeline — complete in source.** The assistant provides push-to-talk microphone capture with echo cancellation, partial and final AssemblyAI transcripts, automatic submission of a completed voice turn to OpenAI, and AssemblyAI TTS playback of the answer. The backend mints separate 60-second STT and TTS tokens; the permanent AssemblyAI key is never included in HTML, JavaScript, logs, URLs, or API responses. The UI exposes listening, thinking, and speaking status plus an explicit stop control. Model, language, and voice defaults come from administrator Settings. Provider validation remains dependent on configuring a real AssemblyAI key after deployment.
4. **Duplicate discovery — complete initial implementation.** Nesti can invoke a read-only duplicate scan for one root available to the requesting user. FilePortal enumerates at most 5,000 safe, non-link files, narrows candidates by size, and calculates SHA-256 locally to prove exact content matches even when filenames differ. A second report normalizes common `copy`, numeric suffix, and extension-preserving name variants so similarly named files are visible even when their bytes differ. OpenAI receives the bounded report, not file content. Results include reclaimable bytes and whether the scan was truncated. Deletion remains a separate existing FilePortal operation with an explicit visual confirmation; the assistant has no deletion tool.
5. **Nesti mascot controller — foundation complete.** The existing blue folder mascot now uses a provider-independent controller with reusable `idle`, `listening`, `thinking`, `speaking`, `moving`, `success`, and `error` states. The assistant drives those states during real connection, microphone, OpenAI, and TTS events. CSS motion is lightweight and fully disabled by `prefers-reduced-motion`. This foundation intentionally keeps Nesti inside its existing layout positions so it cannot cover controls; a later visual-only enhancement can add touch dragging, compact mobile docking, and free screen movement without changing provider or tool code. The approved concept uses the turquoise ribbon as arms and a movement trail.
6. **Hardening and release.** Add API, authorization, encryption, browser, mobile, microphone-denial, provider-failure, and accessibility tests. Document provider costs and data boundaries, publish without overwriting live data or IIS configuration, verify HTTPS and Android behavior, and retain a rollback point.

Initial defaults are push-to-talk, no automatic file-content upload, no automatic deletion, selected/authorized folder scans, and an administrator-controlled global provider configuration. A later phase may add hands-free mode only as an explicit user choice.

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
| [Controllers/FilesController.cs](Controllers/FilesController.cs) | Folder listings, authorized global search, previews, file downloads, uploads, deletion, and ZIP archives |
| [Controllers/PhotosController.cs](Controllers/PhotosController.cs) | Photo timeline, favorites, previews, and library scans |
| [Controllers/AlbumsController.cs](Controllers/AlbumsController.cs) | Album creation and editing |
| [Controllers/SearchController.cs](Controllers/SearchController.cs) | Photo search and camera filters |
| [Controllers/SharingController.cs](Controllers/SharingController.cs) | Public photo and album links checked against the owner's current folder access |
| [Controllers/UsersController.cs](Controllers/UsersController.cs) | User administration, folder assignment, and password changes |
| [Services/FileBrowserService.cs](Services/FileBrowserService.cs) | Configured roots and filesystem authorization shared by file endpoints |
| [Services/AuthorizedFileSearchService.cs](Services/AuthorizedFileSearchService.cs) | Recursive name search constrained to the current user's authorized roots and shared by My Files and Nesti |
| [Services/DuplicateFinderService.cs](Services/DuplicateFinderService.cs) | Read-only authorized root scans, local SHA-256 exact-content grouping, normalized filename grouping, and reclaimable-size reporting |
| [Services/UserStoreService.cs](Services/UserStoreService.cs) | JSON persistence, password hashing, and atomic updates |
| [Services/ThumbnailService.cs](Services/ThumbnailService.cs) | SkiaSharp image thumbnails, FFmpeg video stills, and a bounded in-memory cache |
| [wwwroot/index.html](wwwroot/index.html), [wwwroot/filenest/dashboard.js](wwwroot/filenest/dashboard.js) | FileNest dashboard, recent activity, quick access, and share management |
| [wwwroot/filenest/site.css](wwwroot/filenest/site.css), [wwwroot/filenest/workspace.css](wwwroot/filenest/workspace.css), [wwwroot/filenest/shell.js](wwwroot/filenest/shell.js) | Shared theme and responsive navigation |
| [wwwroot/filenest/nesti.js](wwwroot/filenest/nesti.js) | Provider-independent Nesti state controller for idle, listening, thinking, speaking, movement, success, and error |
| [wwwroot/gallery.html](wwwroot/gallery.html), [wwwroot/app.js](wwwroot/app.js) | Media Gallery with Photos, Videos, Albums, Favorites, and Search |
| [wwwroot/files.html](wwwroot/files.html) | File Manager |
| [wwwroot/login.html](wwwroot/login.html) | FileNest sign-in |
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
| GET | `/api/auth/me` | Signed-in user. Returns `user`, `isAdmin`, `allowedFolders`, and `mustChangePassword`. |

### Files

File endpoints require sign-in and enforce the caller's folder access.

| Method | Route | Behavior |
| --- | --- | --- |
| GET | `/api/files/roots` | Returns configured roots for administrators and available assigned folders for regular users. |
| GET | `/api/files/folder?path=` | Lists folder contents. |
| GET | `/api/files/browse?path=` | Lists subfolders for the administrator folder picker. |
| GET | `/api/files/search?q=&maxResults=` | Searches file and folder names recursively across every authorized root. Queries are 1–200 characters; the default limit is 500 and the maximum is 10,000. |
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
| POST | `/api/users` | Administrator. Creates an account from required `username`, `password`, `firstName`, and `lastName` form fields plus optional `isAdmin`. |
| PUT | `/api/users/{username}` | Administrator. Updates an existing account's required `firstName` and `lastName`; usernames remain immutable. |
| DELETE | `/api/users/{username}` | Administrator. The signed-in account and the last administrator are protected. |
| POST | `/api/users/{username}/promote` | Administrator. Grants the administrator role. |
| POST | `/api/users/{username}/demote` | Administrator. Self-demotion and demotion of the last administrator are blocked. |
| POST | `/api/users/{username}/password` | Users change their own password with `currentPassword` and `newPassword` form fields. Administrators can reset another account with `newPassword`. |
| POST | `/api/users/{username}/temporary-password` | Administrator. Generates and returns a temporary password once, invalidates existing sessions, and requires the target user to replace it at the next sign-in. Administrators must use the normal self-service flow for their own password. |
| GET | `/api/users/{username}/folders` | Administrator. Reads an account's assigned folders. |
| POST | `/api/users/{username}/folders` | Administrator. Replaces folder assignments with a JSON array of paths. |

### AI configuration

All AI configuration endpoints require the administrator role. Responses expose only provider configuration status and non-secret identifiers; saved API keys are never returned.

| Method | Route | Behavior |
| --- | --- | --- |
| GET | `/api/ai/settings` | Returns enabled state, selected models, voice/language defaults, and whether each provider key is configured. |
| PUT | `/api/ai/settings` | Replaces non-secret defaults and optionally replaces either provider key. A blank key keeps the saved value. Enabling requires both keys. |
| DELETE | `/api/ai/settings/keys/{provider}` | Removes the `openai` or `assemblyai` key and disables the assistant. |
| GET | `/api/ai/settings/models` | Returns supported AssemblyAI model choices and, when configured, compatible model identifiers available to the OpenAI key. |
| POST | `/api/ai/settings/test/{provider}` | Validates saved `openai` or `assemblyai` credentials without returning the key or provider token. |
| GET | `/api/ai/status` | Signed-in user. Reports whether text and voice assistant capabilities are enabled plus their non-secret model/language defaults. |
| POST | `/api/ai/chat` | Signed-in user. Sends a message and up to 12 bounded history entries to the configured OpenAI Responses model. Nesti may invoke the authorization-aware `search_files` server tool. The API key and provider response metadata remain server-side. |
| GET | `/api/ai/voice/tokens` | Signed-in user. Mints separate short-lived AssemblyAI streaming-STT and TTS tokens and returns non-secret voice defaults. It never returns the permanent provider key. |
| GET | `/api/ai/duplicates?rootName=` | Signed-in user. Read-only local duplicate scan of one available root display name, limited to 5,000 files. Returns exact SHA-256 content groups and normalized filename groups. |

### Gallery

| Method | Route | Behavior |
| --- | --- | --- |
| GET | `/api/photos/timeline` | Returns a paginated timeline. Set `videosOnly=true` or `favorites=true` to filter it. |
| GET | `/api/photos/{id}/thumbnail` and `/api/photos/{id}/file` | Serve a thumbnail or the original media. |
| POST | `/api/photos/batch/delete` | Administrator. Deletes a selection by media ID and reports failures. |
| POST | `/api/photos/batch/download-form` | Downloads selected media as a ZIP through the browser. |
| POST | `/api/photos/scan` | Administrator. Requests a library scan. |
| GET | `/api/albums` and `/api/search` | List albums or search accessible media. |

### Sharing

| Method | Route | Behavior |
| --- | --- | --- |
| POST | `/api/sharing` | Signed-in user. Creates a photo or album share from `type`, `targetId`, and optional `expiresInDays`. |
| GET | `/api/sharing/mine` | Signed-in user. Lists share links owned by the current account. |
| DELETE | `/api/sharing/{token}` | Owner or administrator. Revokes a share link. |
| GET | `/api/sharing/public/{token}` | Public. Opens a manifest subject to the owner's current folder access. |
| GET | `/api/sharing/public/{token}/photo/{id}/thumbnail` | Public. Serves an item thumbnail when the item belongs to the share. |
| GET | `/api/sharing/public/{token}/photo/{id}/file` | Public. Serves shared media with byte-range support. |

## Frontend and media

The FileNest dashboard opens at `/`. It shows authorized roots, media favorites, and activity recorded by this browser for the signed-in account. Quick Access can pin available roots. The folder storage check reports the count and combined size of files directly in the selected folder. Dashboard searches open authorized results from every available root in File Manager. File Manager can rerun that global search or filter the current folder or result set locally; Gallery searches indexed photos and videos. File Manager is available at `/files.html`, and Gallery at `/gallery.html`. Older gallery bookmarks such as `/#videos` still open the corresponding view. Existing public media links continue to use `/share.html`. The Back arrow on every page returns to the previous browser-history entry and falls back to Home when no previous entry exists.

Dashboard category shortcuts appear in the order Images, Videos, PDFs, Notes, and Documents. Each shortcut opens the matching authorized recursive filter in My Files.

My Files uses the FileNest workspace styling for its folder sidebar, breadcrumbs, upload and selection controls, search, and file tiles. On narrow screens the folder list is available through the **Drives** toggle, and the file grid reflows without horizontal scrolling.

On phones, the sticky top navigation shows Home, My Files, Gallery, and More. More opens Shared, Favorites, Trash, and Settings. The upload dialog uses an authorized root as its destination. For a nested destination, open that folder in My Files and upload there. Shared lists outgoing photo and album links and lets owners revoke them. Deletion is permanent in the current backend, so Trash explains the limitation and disables recovery.

Controls without backend support are disabled and described in the interface. FilePortal currently has no create-folder, rename, or move endpoint; no recycle-bin or restore endpoint; and no notification service. AI summarization and organization remain disabled until an authenticated, server-side provider integration is added. Right-click is temporarily disabled across FileNest and displays an accessible status message; keyboard and touch controls remain available.

Administrators can open Settings to view every account and edit its first and last name after creation. Usernames are fixed identifiers. Role, password, and folder-access changes remain separate controls so their existing authorization and session protections continue to apply.

The administrator-only **AI & Voice** settings panel stores provider keys in encrypted form in the protected data directory. Browsers can save, replace, or remove a key but cannot retrieve it. Model identifiers, voice ID, language, enabled state, and connection status are non-secret. Provider requests originate on the server, except that the later voice phase will mint short-lived browser tokens for AssemblyAI streaming sessions.

Passwords are never stored in readable form and cannot be recovered. An administrator can use **Password → Generate temporary password** for another account. FilePortal displays the generated password only in that response so it can be copied and shared securely; the user store contains only its salted hash. Generation invalidates the account's existing sessions. After signing in with the temporary password, the user is redirected to Settings and all other API operations remain blocked until the temporary password is replaced.

The assistant opens in its own dialog. When AI is disabled it accurately directs administrators to Settings and leaves ordinary file search available. When enabled, its authenticated server endpoint keeps credentials server-side, bounds conversation history, and authorizes every file search through the existing folder policy. File content is not uploaded automatically, and deletion is not exposed as an AI tool. [Assistant module](wwwroot/filenest/assistant.js).

When enabled, the assistant dialog accepts keyboard text or push-to-talk voice input. Voice capture uses a browser AudioWorklet to produce PCM chunks, AssemblyAI marks the completed turn, OpenAI produces the response, and AssemblyAI streams the spoken response. Microphone access requires HTTPS or localhost and remains subject to the browser's permission prompt. Closing the assistant or pressing stop terminates capture and releases the microphone.

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
| `VideoThumbnails__FfmpegPath` | Optional fallback absolute path to the FFmpeg 8 executable. In JSON, use `FfmpegPath` under `VideoThumbnails`. The app first checks `tools\ffmpeg.exe` beside the published application, then this setting and `PATH`. |
| `Email__SmtpUsername` and `Email__SmtpPassword` | Optional SMTP credentials. For Gmail with an app password, use the Gmail address as the username and set the app password as `Email__SmtpPassword`. It is separate from your FilePortal sign-in password. |
| `Email__FromEmail` and `Email__ToEmail` | Sender and recipient for optional sign-in notifications. |
| `Email__SmtpHost`, `Email__SmtpPort`, and `Email__EnableSsl` | SMTP server, port, and TLS. The checked-in settings use `smtp.gmail.com`, port `587`, and TLS enabled. |

OpenAI and AssemblyAI credentials are configured from the administrator Settings page rather than `appsettings.json`. Their encrypted values are stored in `ai-settings.json` under `FilePortal__DataDirectory`; copy the FilePortal Data Protection key ring with the data store when migrating the application to another server.

Protect and back up the data directory. It holds `users.json`, Data Protection keys, gallery metadata, albums, favorites, and share records. It also holds temporary ZIP files, so allow disk space for archives and uploads.

The checked-in root configuration includes **My_Apps F**, mapped to `F:\My_Apps`. Administrators can access configured roots automatically. Regular users receive access only after an administrator assigns that folder, or one of its subfolders, in Settings.

Store SMTP credentials in the hosting environment or a secret manager. Revoke credentials exposed in Git history at their source and replace any reused passwords.

For IIS or Windows-service deployments, place FFmpeg 8 at `tools\ffmpeg.exe` under the publish directory. The hosting identity inherits read/execute access from the published application directory. Alternatively, configure `VideoThumbnails__FfmpegPath` and grant the hosting identity read/execute access to the executable and every parent directory. Restart the app after changing its configuration. If FFmpeg is unavailable, video tiles use the video icon.

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
node --test tests/filenest.browser.mjs
dotnet list FilePortal.csproj package --vulnerable --include-transitive
```

The integration suite publishes to a temporary directory and runs on loopback against disposable accounts and files. It covers legacy user data, concurrent account updates, authorization-aware cross-root search, session revocation, one-time temporary passwords, forced password replacement, uploads, ZIP downloads, image thumbnails, and video byte ranges. With FFmpeg 8 on the test runner's PATH, it also checks frame selection, short clips, and JPEG thumbnails in the gallery, File Manager, and share links. [GitHub Actions workflow](.github/workflows/test.yml).

Before deploying a redesigned build, test responsive navigation at 320–430 CSS pixels and in landscape, plus upload, download, file and video previews, deep links, and session handling with administrator and regular accounts. Test against disposable roots and app data.

After deployment, verify the expected bindings, sign-in, access to each configured root and UNC share, uploads of representative size, and optional SMTP delivery. With video thumbnails enabled, open **Videos** and check that a thumbnail renders and the clip opens when tapped.

## Deployment history

- The .NET 10 migration kept the existing user store. IIS serves the LAN over HTTP and uses HTTPS on localhost. The SMTP replacement credential is configured through the IIS application pool environment.
- The gallery rollout restored signed-in navigation on the LAN, reused the active media index, and kept File Manager at `/files.html`. Follow-up releases added Android layout changes, single and batch deletion, first-to-last range selection, preview rotation, and refresh after files are deleted outside FilePortal.
- On November 18, 2026, the video thumbnail release was deployed to IIS with FFmpeg 8.1.1. Post-deployment checks confirmed the Videos link and sign-in requirement on both bindings and the application pool's access to FFmpeg. Chrome's Android emulation verified rendering a thumbnail and tapping it to open the video on the published build.
- On November 18, 2026, the `redesign/filenest` branch was merged into `main` and both branches were pushed to GitHub. Commit `46ff168` contains the complete FileNest redesign and commit `b77f08a` contains the final reproducible browser validation.
- The browser regression suite uses Chrome and verifies the sign-in flow, dashboard, assistant dialog, File Manager search and desktop sidebar, plus responsive layouts with no horizontal overflow at 320–430 px. Windows GitHub Actions runs it after the release integration suite.
- `http://127.0.0.1:5286/` is a temporary redesign preview process. It is not a live deployment and may be stopped independently of IIS or the Windows service.
- `http://192.168.5.7/` is the machine-LAN live address. `http://127.0.0.1:5286/` is only the temporary redesign preview and is not the deployment target. The live IIS site now points to the merged FileNest publish.
- The merged `.NET 10` publish was created in `bin\Release\net10.0\publish` and deployed from `bin\Release\net10.0\live-publish`. Production data was copied from the current publish, and the users-store hashes matched.
- The Windows service named `FilePortal` was found stopped and still configured through NSSM to an obsolete `.NET 8` executable. The active live deployment is IIS (`FilePortalPool`), so the Windows service was left stopped and was not used for the IIS rollout.
- Video thumbnails use a frame around two seconds through FFmpeg 8.1.1, then fall back to the opening frame for short clips. On November 18, the live thumbnail issue was traced to the IIS app-pool identity lacking read/execute access to FFmpeg under the interactive user's WinGet profile. The resolver now prefers an app-local `tools\ffmpeg.exe`; deployment instructions require placing FFmpeg beside the published app so the hosting identity can execute it.
- The live IIS site was switched to `bin\Release\net10.0\live-publish` with its existing port 80 and localhost HTTPS bindings preserved. The current users, Data Protection keys, favorites, shares, and photo index were copied from the previous publish; the users-store hashes matched. FFmpeg 8.1.1 is bundled under the new publish's `tools` directory with app-pool read/execute access. The integration suite verifies a real JPEG from around two seconds and opening-frame fallback for short videos.
- After the switch, the LAN homepage, Gallery, and FileNest assets returned HTTP 200. The app-local FFmpeg executable and IIS app-pool permissions restore video still generation around two seconds into a clip, with an opening-frame fallback for short clips. The signed-in Videos page was visually rechecked on the live LAN site and the user confirmed that thumbnails are working.
- On November 19, 2026, My Files was updated to use the responsive FileNest workspace layout. The static page and shared workspace stylesheet were deployed to the live IIS publish without restarting the app or changing production data. Source and deployed file hashes matched; the signed-in live page loaded its file list without horizontal overflow.
- On November 19, 2026, the Dashboard category row was deployed in the order Images, Videos, PDFs, Notes, and Documents, and Settings gained an administrator-only editor for existing users' first and last names. Integration and Chrome browser tests passed before release. The IIS update preserved the production user-store hash, application settings, FFmpeg tools, HTTPS redirect, and explicit ANCM DLL argument; HTTPS returned 200 and LAN HTTP continued redirecting to the secure hostname.
- On November 19, 2026, secure account recovery was deployed. Administrators can generate an 18-character temporary password that is returned once, invalidates the target account's sessions, and marks the account for mandatory password replacement. Accounts using a temporary password can access only account details, sign-out, and their own password-change endpoint until replacement. The release passed integration and Chrome UI tests; IIS preserved the production user-store hash, configuration, HTTPS rewrite, certificate setup, and FFmpeg tools. Live Home and Settings returned HTTPS 200 after deployment.
- On November 19, 2026, the configured root **My_Apps F** (`F:\My_Apps`) was added to source control and the live IIS settings. The directory existed and was readable, the production user-store hash and HTTPS configuration were preserved, HTTPS returned 200 after restart, and LAN HTTP continued redirecting to the secure hostname. Administrators see the root automatically; regular users require an explicit folder assignment.

### Live hosting state after HTTPS setup (November 19, 2026)

- The live site is IIS site `FilePortal`, app pool `FilePortalPool`, with physical path `D:\Dev\Fileportal\bin\Release\net10.0\live-publish`. The separate Windows service named `FilePortal` is not the active host; do not reconfigure or start it as part of an IIS deployment.
- The hostname is `miportal.naranjoestudio.com`. Its A record resolved to `98.55.227.19` during setup; this is only the observed address on that date and may change. The router restart restored the previously working external HTTP access.
- IIS bindings after setup were verified as:
  - `http *:80:` (existing catch-all)
  - `http *:80:miportal.naranjoestudio.com` (added for ACME/IIS hostname selection)
  - `https 127.0.0.1:443:` (pre-existing local binding; preserve it)
  - `https *:443:miportal.naranjoestudio.com` with SNI enabled (`sslFlags=1`)
- A Let's Encrypt certificate was not used. Win-ACME (simple-acme) registered an ACME account with ZeroSSL and successfully completed HTTP-01 self-hosted validation for `miportal.naranjoestudio.com`. The certificate was stored in the Local Computer `WebHosting` certificate store, and the IIS installation plugin added the hostname/SNI HTTPS binding. Win-ACME created the scheduled renewal task `win-acme renew (acme.zerossl.comv2DV90)`; preserve the task and `C:\Tools\win-acme` installation. The setup was interactive and the account contact email was provided in the ACME client; do not copy private account details into source control.
- The router must forward TCP 80 and 443 to the server's reserved LAN address `192.168.5.7` on the same ports. Port 443 was reachable in a TCP check after certificate setup. HTTP is still needed for ACME HTTP-01 renewal and redirects; do not remove its forwarding rule.
- IIS URL Rewrite was installed on the host. The root `web.config` and the active publish's `web.config` contain a permanent (301) redirect from HTTP to `https://miportal.naranjoestudio.com/{path}`, preserving the query string. Both the HTTP-to-HTTPS 301 and HTTPS 200 were verified after the repair below. Keep these two config files in sync when publishing.
- During setup, restarting IIS after deploying the redirect initially produced HTTP 502.5. The Application log showed ANCM trying to run bare `dotnet` with no application argument (`Usage: dotnet [path-to-application]`). The final `web.config` explicitly sets `arguments=".\FilePortal.dll"`; removing it will break application startup. `stdoutLogEnabled` is currently `true` and `.\logs` was created in the active publish to aid startup troubleshooting. After confirming stable operation, consider disabling stdout logging to prevent unbounded diagnostic logs.
- The user reported that the Windows PC firewall and router firewall are disabled; the modem has no firewall. This was not changed during HTTPS setup. Re-enable host firewall protection and allow only the required inbound IIS ports (TCP 80/443) rather than leaving the firewall disabled.
- For future IIS releases, preserve the hostname HTTP/HTTPS bindings, certificate renewal task, URL Rewrite rule/module, and the explicit ANCM DLL argument. Do not run `setup-iis.ps1` blindly over the live site: its new-site defaults are `127.0.0.1:5251` and it does not provision this domain's certificate or redirect.
