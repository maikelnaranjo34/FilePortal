# Verification

Run `node --test tests/fileportal.test.mjs` from the repository. The suite publishes the application, provisions an administrator, creates disposable files and accounts, and starts a loopback host. It checks authorization, session revocation, legacy user data, concurrent user changes, password updates, uploads, range reads, image thumbnails, and ZIP downloads.

For a deployment smoke test, verify the HTTPS binding, service account access to each configured root and UNC share, an upload of representative size, and optional SMTP delivery.
