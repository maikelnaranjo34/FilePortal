# Security implementation

- Administrator provisioning runs locally. Sign-in authenticates against the user store using salted PBKDF2 hashes. The application validates each cookie against the account's current security stamp.
- Administrators operate inside configured roots. Regular users operate inside assigned folders underneath those roots. Each file endpoint checks access before filesystem operations. Linked paths and application data are excluded.
- The user store serializes updates and replaces its JSON file atomically. Malformed data returns an error and remains available for recovery.
- Set narrowly scoped filesystem permissions on the hosting identity. Use HTTPS for remote access and keep administrator accounts limited to operators. Local writers with access to the same filesystem require the same trust and OS permission controls as the host.
- ZIP creation uses the protected data directory for temporary files and cleans them up when the response closes. Plan disk capacity for archives and uploads.

See the [integration tests](tests/fileportal.test.mjs) for verified behavior and the [deployment guides](README.md#configuration-and-upgrades) for hosting configuration.
