# Run FilePortal as a Windows service

Publish the .NET 10 Windows executable.

```powershell
dotnet publish FilePortal.csproj -c Release -r win-x64 --self-contained false
```

Install the current ASP.NET Core 10 runtime on the host. Configure roots and preserve existing user data as described in the [upgrade instructions](README.md#configuration-and-upgrades). If the deployment needs a new administrator, run `dotnet FilePortal.dll --provision-admin your-admin-name` from the publish directory.

From an elevated PowerShell session, run `install-service.ps1 -Start`. The script uses the bundled NSSM, sets the application's working directory, and configures the service to listen on `http://127.0.0.1:5251` by default. Use `-PublishPath` for a custom directory. Stop an existing service before reconfiguring it.

The service runs as Local Service. Grant `NT SERVICE\FilePortal` the filesystem access required for the configured roots and any custom data directory. To expose FilePortal on the network, use an HTTPS reverse proxy or configure an HTTPS endpoint. Service logs are written to `%ProgramData%\FilePortal\logs`.
