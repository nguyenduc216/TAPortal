# Blazor migration test-environment provisioning

Use a dedicated non-production `TAPortalSys` instance with synthetic data or an explicitly approved sanitized backup. Never copy production personal data by default.

1. Provision a database/server identity restricted to the staging database and TLS 1.2+.
2. Apply the approved TAPortalSys migration manifest and run its read-only verify scripts.
3. Supply connection data through `Database__ConnectionString` in the staging secret store; do not edit committed appsettings.
4. Create synthetic Admin Test, Limited Permission Test and DENY-precedence Test accounts through an approved fixture owned by the staging operator.
5. Store browser credentials/storage state outside the repository and artifact share. Rotate them after the verification window.
6. Record fixture IDs in an external run manifest. Reset by restoring a known staging snapshot or running an approved ID-scoped cleanup transaction.
7. Set `TAPORTAL_E2E_BASE_URL` and external `TAPORTAL_E2E_STORAGE_STATE`, execute the Phase 7 Playwright suite, then run permission and CRUD matrices.
8. Verify no screenshots, traces, logs or publish artifacts expose credentials, hashes, connection strings or personal data before sharing.

Current environment has SQL connectivity but no authorized test account, no approved write/reset fixture, and no verified IIS staging target. Authenticated and destructive browser checks remain blocked.
