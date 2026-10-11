# TAPORTAL-20261011-BLAZOR-MIGRATION-004 Security Proposal

This document records follow-up actions only. No security infrastructure or production data was changed by the migration implementation.

1. Rotate any SQL credentials previously placed in plaintext configuration or shared outside the trusted secret boundary.
2. Store replacement secrets in the approved secret manager or environment configuration; keep `appsettings.json` and publish artifacts secret-free.
3. Have the SQL Server/network owner confirm TLS 1.2+ and certificate validation, then disable legacy TLS only through an approved change window.
4. Restrict the public SQL endpoint using network controls and least-privilege SQL principals.
5. Resolve OperationalDb table ownership separately; do not create tables or migrate data as part of this UI change.