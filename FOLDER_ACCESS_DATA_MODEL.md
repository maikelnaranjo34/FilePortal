# Folder access data model

Each account in `users.json` includes an `AllowedFolders` array of fully qualified directory paths. An empty array grants a regular account no file access. The service requires assigned folders to be within a configured root. Assignments are checked against the live store on requests.

The account also stores a salted PBKDF2 hash and a `SecurityStamp` for validating sessions. Older records without a stamp are accepted on sign-in. Role and password changes write a new stamp. Preserve the user store when deploying a new version and manage assignments through the portal.
