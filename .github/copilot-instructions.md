# FilePortal - AI Coding Agent Instructions

## Project Overview

**FilePortal** is a multi-root file browser web application that aggregates access to:
- Local Windows drives (E:, F:, H:)
- Network UNC shares (\\192.168.5.1\h, \\192.168.5.1\g)
- Syncthing-synced Samsung S20 phone files (D:\Samsung S20_Cloud\WebsiteBrowser\Files)

**Tech Stack**: ASP.NET Core 8.0 + vanilla JavaScript frontend

## Architecture & Data Flow

### Backend (ASP.NET Core)
- **Program.cs**: Configures DI (FileBrowserService), JSON camelCase serialization, static files, fallback to index.html on unknown routes
- **FilesController.cs** & **FileBrowserService.cs**: Dual implementations (controller has inline logic, service has class-based logic) - both need sync when changes made
- **Port**: http://0.0.0.0:5251

### Frontend
- **index.html**: Vanilla JS SPA with dark theme (Tailwind-like styling)
- **Data flow**: Root selector (sidebar) → folder list (main area) → navigate via breadcrumb or double-click folders
- **Key JS functions**: `onRootClick(root)`, `onItemClick(item)`, dynamic breadcrumb path building

### Models
- **FileItem**: Name, FullPath, RelativePath, IsDirectory, Size, LastModified
- **RootItem**: Name, Path (appears in appsettings.json as "Roots" config)

## Critical Developer Workflows

### Build & Run
```bash
# Restore and build
dotnet build

# Run dev server (watches for changes)
dotnet run

# Access UI: http://localhost:5251
```

### Configuration
- **appsettings.json**: Defines "Roots" - key-value pairs (display name → real path)
- **FileBrowserService**: Has hardcoded `_allowedDriveRoots` array and `_specialRoots` dictionary
- **FilesController.cs**: Also has hardcoded paths at class level - UPDATE BOTH when adding roots

### Authentication (WIP)
- **AuthController.cs**: Has hardcoded credentials (maikel / Cafeta0823$)
- Uses ASP.NET Core Cookie authentication (7-day expiry)
- login.html exists but integration not complete - endpoints ready but frontend wiring incomplete

## Key Patterns & Conventions

### Path Handling
- Uses `Path.Combine()` and `Path.GetRelativePath()` for cross-platform safety
- **RelativePath** stored in FileItem for UI navigation (avoids exposing full system paths)
- UNC paths (\\192.168.5.1\h) work directly without drive letter mappings

### Error Handling
- Service methods catch directory/file access exceptions and skip problem items (silent fail)
- API returns 400 for invalid input, 404 for missing paths, 500 for unhandled errors

### JSON Serialization
- **All properties must use camelCase for JS frontend** (configured in Program.cs via JsonSerializerOptions.PropertyNamingPolicy)
- Output models must match this (e.g., FileItem.IsDirectory → isDirectory in JSON)

### Controller Routes
- GET /api/files/roots → list all available roots
- GET /api/files/folder?path={path} → list items in folder
- POST /api/auth/login, /api/auth/logout → authentication

## Important Notes

### Code Duplication
FilesController.cs and FileBrowserService.cs implement overlapping file browsing logic. **When modifying file listing, root resolution, or path handling—update both files** to stay synchronized.

### Hardcoded Paths
Three locations define special root paths and must be kept in sync:
1. appsettings.json Roots section
2. FileBrowserService._specialRoots dictionary
3. FilesController PhoneRoot, RouterYPath, RouterZPath static fields

### UI Conventions
- Active root/item highlighted with sky blue (#0ea5e9)
- Sidebar width: 260px, dark theme with #1f2937 borders
- Responsive: uses flexbox, fallback to index.html for SPA routing

## Debugging Tips

- Check browser DevTools Network tab for API responses (watch for path encoding issues)
- Verify UNC path accessibility on Windows: `net use` commands may be needed for credential caching
- FileBrowserService logs nothing—add Debug.WriteLine() if needed
- appsettings.Development.json may override appsettings.json locally
