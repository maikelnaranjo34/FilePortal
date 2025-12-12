Short secrets guide
===================

This project reads authentication credentials from environment variables (preferred) or from `appsettings.json` under `Auth:Username` / `Auth:Password`.

Local (temporary, current PowerShell session)
-------------------------------------------
Run in PowerShell to set for the current session only:

```powershell
$env:Auth__Username = "maikel"
$env:Auth__Password = "your-secret-here"
dotnet run
```

Local (persist for current user)
--------------------------------
Use `setx` to persist the variable for the user (restart terminal to pick up):

```powershell
setx Auth__Username "maikel"
setx Auth__Password "your-secret-here"
# Restart your terminal/session
```

Windows GUI (persistent, system/user)
-------------------------------------
Open "Edit the system environment variables" → "Environment Variables" and add `Auth__Username` / `Auth__Password` under User or System variables.

GitHub Actions (recommended for CI/CD)
-------------------------------------
1. Repo → Settings → Secrets → Actions → New repository secret
2. Add `Auth__Username` and `Auth__Password` as secrets
3. Use them in a workflow (example):

```yaml
env:
  Auth__Username: ${{ secrets.Auth__Username }}
  Auth__Password: ${{ secrets.Auth__Password }}
```

Notes
-----
- Do not commit real secrets to the repository. Leave `appsettings.json` empty or use placeholders.
- The environment variable name `Auth__Username` maps to `configuration["Auth:Username"]` in ASP.NET Core.
- For production consider a managed secret store (Azure Key Vault, AWS Secrets Manager, etc.).
