# Deploy FilePortal on IIS

Install IIS and the current .NET 10 Hosting Bundle. Publish the app from the repository.

```powershell
dotnet publish FilePortal.csproj -c Release
```

The publish directory is `bin\Release\net10.0\publish`. Configure the deployed roots and any secret environment variables. To upgrade an existing site, preserve its `users.json` or point `FilePortal__DataDirectory` at its existing protected data directory. For a new store, provision the administrator from the publish directory before starting the site.

```powershell
dotnet FilePortal.dll --provision-admin your-admin-name
```

Run `setup-iis.ps1` from an elevated PowerShell session. A new site uses a loopback binding on port 5251. The script preserves the bindings of an existing FilePortal site. Use `-PublishPath` for a different publish directory.

Use an IIS HTTPS binding and a valid certificate for network access. Give the app pool identity read access to the published application, modify access to the data directory, and the filesystem and share permissions required by the configured roots. For UNC paths, both share and NTFS permissions must allow the app pool identity or its configured service account. If an older script granted `IIS_IUSRS` broad access, review and remove that grant when it is no longer required.

Use `check-iis-setup.ps1` to inspect the site, bindings, application pool, and publish files. To diagnose startup failures, temporarily enable ANCM stdout logging and give the app pool identity write access to the log directory. IIS request filtering also controls the maximum accepted upload size independently of the application.
