# FilePortal

FilePortal targets .NET 10 and serves a vanilla JavaScript frontend from `wwwroot`. `index.html` is the main browser and `users.html` handles account management.

`FilesController` uses `FileBrowserService` for path authorization. Keep every new file endpoint within that service's scope. User records and updates go through `UserStoreService`. Login and the cookie validator use the account's current security stamp.

Build with `dotnet build` and run `node --test tests/fileportal.test.mjs` to verify the published application. See [README](../README.md) for operator setup and credentials.
