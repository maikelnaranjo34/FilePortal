# FilePortal quick reference

| Task | Location |
| --- | --- |
| Configure drives and UNC shares | `Roots` in `appsettings.json` |
| Provision the first administrator | `dotnet run -- --provision-admin your-admin-name` |
| Manage an account's file access | **Manage Users** ? **Folders** |
| Change your password | **My Account** |
| Run the integration suite | `node --test tests/fileportal.test.mjs` |

See [setup and configuration](README.md) for credentials, data migration, and publishing.
