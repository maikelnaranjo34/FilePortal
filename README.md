FilePortal
=========

Quick notes for running and configuring authentication and thumbnails.

Run (development):

```powershell
cd d:\Dev\Fileportal
dotnet restore
dotnet run
```

Auth configuration
------------------
- The app reads credentials from the following environment variables (preferred):
  - `Auth__Username` (or `AUTH__USERNAME`)
  - `Auth__Password` (or `AUTH__PASSWORD`)

- If those are not set, the app will fall back to values in `appsettings.json` under the `Auth` section.
- For security, `appsettings.json` should not contain production secrets; prefer environment variables or a secret manager.

Example (PowerShell) to set creds for local dev session:

```powershell
$env:Auth__Username = "maikel"
$env:Auth__Password = "your-secret-here"
dotnet run
```

Thumbnails
----------
- Image thumbnails are generated and cached under the system temp directory (`Path.GetTempPath()`) in a `FilePortalThumbnails` folder.
- The app uses ImageSharp to resize images and cache them as JPG files for faster subsequent requests.

Notes about committing changes
-----------------------------
- This workspace was modified by the assistant to add thumbnail caching (ImageSharp) and to prefer env vars for auth.
- Git may not be available in the environment where these edits were applied; run the following locally to commit:

```powershell
cd d:\Dev\Fileportal
git add -A
git commit -m "Auth: prefer env vars; add thumbnail caching; add logout UI"
```

If you want, I can update this README with deployment examples (Docker, systemd, IIS) — tell me which target you need.
