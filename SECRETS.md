# Secrets

Provision administrator accounts using the local command in the [setup instructions](README.md#run-locally). It prompts for a password and stores a salted hash in the user store. Protect the data directory and include it in backups.

Supply optional email credentials using the hosting environment or a secret manager. Set `Email__SmtpUsername`, `Email__SmtpPassword`, `Email__FromEmail`, and `Email__ToEmail`. Host, port, and TLS are configurable through `Email__SmtpHost`, `Email__SmtpPort`, and `Email__EnableSsl`.

Revoke the SMTP app password that was committed previously. Change any account that used the former default administrator password, including accounts outside FilePortal that shared it. Historical commits still contain the retired values.
