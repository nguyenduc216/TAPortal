# Phase 8 staging runbook

FEATURE_ID: `TAPORTAL-PLATFORM-BLAZOR-MIGRATION`
CHANGE_ID: `TAPORTAL-20261011-BLAZOR-MIGRATION-008`

This runbook is for an approved non-production environment only. Phase 8 does not change production routes, schema, credentials, firewall rules, or IIS.

## 1. Staging configuration

1. Deploy the contents of `publish-TAPortal-phase8` to a dedicated IIS application pool using **No Managed Code** and the .NET 8 Hosting Bundle.
2. Set `ASPNETCORE_ENVIRONMENT=Staging` in IIS/environment configuration.
3. Enable the WebSocket Protocol Windows feature and WebSockets for the IIS site. Use HTTPS with a trusted staging certificate.
4. Keep a single application instance unless sticky sessions or an approved SignalR backplane is configured.
5. Supply all required configuration from environment variables or an approved secret provider. The publish output intentionally contains no `appsettings.json`.

Required secret-backed settings include `Database__ConnectionString`. Keep `Bootstrap__EnsureAdmin=false`. Never place passwords, storage state, or connection strings in source, deployment ZIPs, logs, screenshots, or test reports.

## 2. Account and test-data approval

Operations/QA must create synthetic staging users through the approved administration process, not by production bootstrap or ad-hoc SQL. Provision separate full-access and restricted accounts with documented permission expectations. Store credentials in the approved secret vault. Create a unique test-data prefix (for example `E2E-<run-id>`) and record all created row IDs for cleanup.

## 3. SQL connectivity preflight

From the staging host, verify DNS/private routing, TCP access, TLS 1.2+ negotiation, trusted certificate validation, and least-privilege database login. Start the app and confirm a read-only login/dashboard request. Reject the release if SqlClient reports TLS downgrade, certificate bypass, plaintext credentials, the wrong database, or missing required tables. Do not run migrations for this UI-only change.

## 4. Playwright

From `tests/playwright`:

1. Run `npm ci` and `npx playwright install chromium`.
2. Set `TAPORTAL_E2E_BASE_URL` to the approved staging URL.
3. Log in interactively with the synthetic account and save storage state **outside the repository**; set its absolute path in `TAPORTAL_E2E_STORAGE_STATE`.
4. Set the capability flags documented in the Playwright README. Set `TAPORTAL_E2E_ALLOW_WRITES=true` only for an isolated, resettable staging database.
5. Run `npm test`. A skipped authenticated or write test is pending evidence, not PASS.

## 5. SignalR, WebSocket, refresh and reconnect

In browser developer tools confirm `/_blazor` upgrades to WebSocket (HTTP 101), no repeated negotiate failures occur, and the reconnect modal exists exactly once. Exercise refresh and a second tab. To test reconnect, interrupt the browser's staging network briefly, verify the blocking reconnect state appears, restore connectivity, and confirm recovery without duplicate submissions. If recovery fails, use reload and retain console/network traces.

## 6. Cleanup and evidence

Use the approved application actions or a reviewed staging-only reset fixture to remove rows with the run prefix in dependency-safe order. Never point cleanup at production. Capture commit SHA, artifact hash, environment name, timestamp/timezone, account role (not identity/password), test counts, skipped tests, screenshots/traces, WebSocket evidence, SQL TLS result, created/deleted row IDs, and cleanup confirmation. Store evidence in the restricted release location.

## 7. Rollback

Stop the staging site, restore the previous immutable artifact and its matching secret/configuration version, restart the staging app pool, then repeat login, dashboard, SQL and SignalR smoke checks. Restore the staging database only from its approved pre-test snapshot when write tests changed data. Phase 8 never authorizes a production rollback or deployment.

## Readiness gate

Staging is ready only when secret injection, trusted TLS, database ownership, synthetic accounts, cleanup, authenticated Playwright, WebSocket and reconnect evidence all pass. Until then the Phase 8 runtime result is **BLOCKED/PENDING**, overall status is **PARTIAL**, and route cutover is **NO**.
