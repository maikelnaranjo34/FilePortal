# Getting started

Follow the [local setup instructions](README.md#run-locally) to configure roots, provision the first administrator, and start FilePortal. For a published instance, run `dotnet FilePortal.dll --provision-admin your-admin-name` from the publish directory with the same data configuration the host uses.

After signing in as an administrator, open **Manage Users**, add a regular user, and choose **Folders** beside the new account. Select one or more folders from the configured roots and save. The account can sign in immediately and access the assigned folders. An empty assignment grants no file access.

Use **My Account** or the account controls under **Manage Users** to change your own password. A password or role change invalidates the affected user's active sessions.

See [IIS deployment](IIS-DEPLOYMENT-README.md) and [Windows service deployment](SERVICE-README.md) for hosting instructions.
