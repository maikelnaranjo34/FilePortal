# FilePortal — Project Reference

## What This Is

FilePortal is an ASP.NET Core 8 web app that serves as a private file browser for a home Windows server. It lets authenticated users browse, preview, upload, download, and delete files across multiple local drives and network shares. It runs on IIS and is accessed over the local network at `http://192.168.5.7`.

## Running & Building

```bash
# Build (Release)
dotnet build --configuration Release

# Run locally for testing (listens on http://localhost:5000)
dotnet bin/Release/net8.0/FilePortal.dll

# Restart the IIS site (run as Administrator)
restart-site.bat
```

The IIS site uses the DLL directly from `bin\Release\net8.0\FilePortal.dll` — no separate publish step is needed after `dotnet build`.

## Project Structure

```
Controllers/
  AuthController.cs      # Login, logout, /api/auth/me
  FilesController.cs     # All file operations (browse, upload, download, delete, thumbnails)
  UsersController.cs     # User management + folder access control
Models/
  UserRecord.cs          # User data model (includes AllowedFolders)
  FileItem.cs            # File/folder DTO
Services/
  UserStoreService.cs    # JSON-based user persistence, PBKDF2 password hashing
  FileBrowserService.cs  # File system helpers (legacy, not heavily used)
wwwroot/
  index.html             # Main portal UI — primary frontend
  users.html             # Admin user management UI
  portal.html            # Alternative UI (not active)
  login.html             # Standalone login (not active)
Program.cs               # Middleware pipeline, cookie auth, DI setup
appsettings.json         # Email config, drive roots
web.config               # IIS AspNetCore Module config
restart-site.bat         # Quick IIS restart script (run as admin)
logs/                    # IIS stdout logs (stdout_YYYYMMDD_PID.log)
data/                    # Created at runtime inside bin/Release/net8.0/
  users.json             # User accounts (auto-created)
```

## IIS Deployment

- **Site name:** FilePortal
- **App pool:** FilePortalPool (ApplicationPoolIdentity, No Managed Code)
- **Binding:** `http://192.168.5.7:80`
- **Physical path:** `D:\Dev\Fileportal`
- **Hosting model:** Out-of-process (IIS proxies to dotnet.exe)
- **Stdout logs:** `.\logs\stdout` (enabled in web.config)

### Known IIS Gotcha — .NET Updates Break the Site

When a new .NET SDK or runtime is installed, it can reset permissions on `C:\Program Files\dotnet\`, removing `IIS_IUSRS` execute access. This causes a **503 Service Unavailable** even though the app pool shows as "Started".

**Fix (run as Administrator):**
```
icacls "C:\Program Files\dotnet" /grant "IIS_IUSRS:(OI)(CI)(RX)" /t
```

Then recycle the FilePortalPool app pool. The site should come back immediately.

**Diagnosis:** If stdout logs in `logs/` stop being created after a .NET update, this is the cause. The app runs fine when launched directly (`dotnet FilePortal.dll`) because the current user is in BUILTIN\Users — the problem is specific to the IIS app pool virtual account.

## API Endpoints

### Auth — `/api/auth`
| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `/login` | None | Login with form data (username, password). Creates first admin on first run. |
| POST | `/logout` | Yes | Clears auth cookie. |
| GET | `/me` | Yes | Returns `{ user, isAdmin, allowedFolders }` |

### Files — `/api/files`
| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/roots` | Yes | Drive roots. Non-admins see only their allowed folders. |
| GET | `/folder?path=` | Yes | List folder contents. Returns 403 if no access. |
| GET | `/browse?path=` | Yes | List subdirectories only (for admin folder picker UI). |
| GET | `/category?path=&category=&maxResults=` | Yes | Recursive search by category (pictures/videos/documents). |
| GET | `/thumbnail?path=` | Yes | Returns thumbnail image or SVG icon. Cached in `%TEMP%\FilePortalThumbnails\`. |
| GET | `/file?path=` | Yes | Stream file content (download/open). |
| POST | `/upload?targetPath=` | Yes | Multipart upload, up to 10 GB. |
| DELETE | `/delete?path=` | Yes | Delete file or directory (recursive). |
| POST | `/download-zip` | Yes | Batch ZIP download. Body: `{ paths: [...] }` |

### Users — `/api/users`
| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/init` | None | `{ isEmpty: bool }` — first-run check. |
| POST | `/init` | None | Create initial admin (only if no users exist). |
| GET | `/` | Admin | List all users. |
| POST | `/` | Admin | Add new user. |
| DELETE | `/{username}` | Admin | Delete user. Cannot delete self or last admin. |
| POST | `/{username}/promote` | Admin | Make admin. |
| POST | `/{username}/demote` | Admin | Remove admin. Cannot demote self or last admin. |
| POST | `/{username}/password` | Yes | Change password. Admins can change any; users change own (must supply current). |
| GET | `/{username}/folders` | Admin | Get user's allowed folders. |
| POST | `/{username}/folders` | Admin | Set user's allowed folders (JSON array of paths). |

## Authentication & Users

- **Cookie auth:** 7-day sliding expiration
- **First run:** First login creates an admin account automatically from the submitted credentials
- **Password hashing:** PBKDF2-SHA256, 16-byte random salt, 100,000 iterations
- **Storage:** `data/users.json` relative to `AppContext.BaseDirectory` (i.e., `bin/Release/net8.0/data/users.json`)
- **Roles:** `Admin` (full access) or regular user (limited to assigned folders)

## Folder Access Control

- Each `UserRecord` has `AllowedFolders: List<string>` (full paths)
- **Admins:** Bypass all folder restrictions (AllowedFolders is ignored)
- **Regular users with no folders assigned:** Cannot browse anything (empty sidebar)
- **Regular users with folders assigned:** See only those folders as roots; all file operations check access
- Path comparison uses `Path.GetFullPath()` + `OrdinalIgnoreCase` to prevent traversal attacks
- Set via the Users UI (admin only) or `POST /api/users/{username}/folders`

## Drive Roots Configuration

Configured in `appsettings.json` under `"Roots"`. Friendly name → absolute path:

```json
"Roots": {
  "D": "D:\\",
  "E": "E:\\",
  "H": "H:\\",
  "J": "J:\\",
  "K": "K:\\",
  "Data": "G:\\",
  "Easystore": "I:\\",
  "Samsung S20": "D:\\Samsung S20_Cloud\\WebsiteBrowser\\Files"
}
```

G:, I:, E:, H:, J:, K: are external/removable drives — they may not always be present. The UI only shows roots that exist at request time.

## Email Notifications

Login events send an email via Gmail SMTP. Config in `appsettings.json`:
```json
"Email": {
  "SmtpHost": "smtp.gmail.com",
  "SmtpPort": 587,
  "SmtpUsername": "maikelnaranjo34@gmail.com",
  "SmtpPassword": "<gmail-app-password>",  // NOT the regular password
  "FromEmail": "maikelnaranjo34@gmail.com",
  "ToEmail": "maikelnaranjo34@gmail.com",
  "EnableSsl": true
}
```

The password must be a Gmail **App Password** (generated in Google Account → Security → 2FA → App Passwords). The regular Gmail password will not work.

## Thumbnails

- Cached to `%TEMP%\FilePortalThumbnails\` with SHA1 key (filepath + mtime)
- Max 240×240 px, JPEG quality 80
- Auto-invalidated when the file's modification time changes
- SVG icons returned for: folders (purple), videos (purple play), audio (blue), documents (gray), unknown (gray)
- Supported image types: jpg, jpeg, png, gif, bmp, webp, svg, ico, tiff

## Frontend Notes

- **index.html** is the active main UI. `portal.html` is an alternative design that is NOT currently wired up.
- JSON responses use **camelCase** (configured globally in `Program.cs`)
- The UI checks `/api/auth/me` on load; redirects to login modal on 401
- Keyboard navigation: press a letter key to jump to files/folders starting with that letter; press again to cycle

## Tech Stack

- ASP.NET Core 8.0 (net8.0)
- ImageSharp (SixLabors) for thumbnail generation
- Swashbuckle for Swagger (available in Development environment only)
- Vanilla JS frontend — no build step, no npm
- No database — JSON file for user data, file system for everything else
