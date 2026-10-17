# Phase 9 local runtime verification

FEATURE_ID: `TAPORTAL-PLATFORM-BLAZOR-MIGRATION`
CHANGE_ID: `TAPORTAL-20261011-BLAZOR-MIGRATION-009`
Date: 2026-10-11 (Asia/Bangkok)

## Executed evidence

- Started the Release build through Kestrel with the repository web project as content root and Development configuration on an ephemeral loopback port.
- .NET SDK `8.0.425` and ASP.NET Core runtime `8.0.31` were present.
- The application started successfully. A single synthetic invalid login reached the configured read-only authentication lookup and failed closed; no CRUD or production-data write was performed.
- SQL connectivity succeeded, but SqlClient emitted a negotiated TLS 1.0 security warning. TLS remediation remains owned by SQL/network operations.
- Chromium rendered the login page at 1440×900, 768×1024 and 390×844. Automated assertions and visual review confirmed no horizontal overflow, usable form layout, loaded logo asset, Tabler icon font, portal CSS and password-toggle JavaScript.
- Browser observers found no unexpected page exceptions, console errors or failed requests on login and anonymous authorization flows.
- All protected MVC/Blazor routes redirected anonymous access to login.
- The initial unknown-route check exposed a blank HTTP 404 response. Phase 9 added a branded, anonymous status-code page while preserving HTTP 404; the rerun passed and the screenshot was visually reviewed.
- Screenshot/trace/video evidence remains under ignored `tests/playwright/test-results`; it contains anonymous pages only and is not committed.

## Automated results

- Release solution build: PASS, 0 warnings, 0 errors.
- .NET/bUnit suite: PASS, 76/76.
- Phase 9 Chromium suite: PASS, 6/6.
- Full Playwright suite: 7 PASS, 8 SKIPPED.
- The eight skipped tests require an authorized external storage state and/or approved staging controls; they are not counted as PASS.

## Blocked local runtime scope

No `TAPORTAL_E2E_STORAGE_STATE` was configured. Authenticated Dashboard/Profile/Password/Users/Roles/Menus/Customers/Departments/Teams, Sidebar/Topbar, Interactive Server events, SignalR transport, Global Loading, reconnect, refresh, logout and multi-tab runtime checks remain BLOCKED. Authentication was not bypassed and no test account was created.

## User/operations checklist — server-only evidence

Do not repeat the anonymous local checks above. Perform these checks only on the authorized staging/server environment:

| Check | Procedure | Expected result | Evidence |
|---|---|---|---|
| Domain and HTTPS | Open the staging URL and inspect the certificate chain/security panel | Trusted certificate, correct hostname, no mixed content | Certificate screenshot/export and browser network summary |
| IIS application pool | Confirm .NET 8 Hosting Bundle, No Managed Code, identity and environment settings; recycle once in an approved window | App restarts, health/login returns normally, no configuration loss | IIS configuration export and timestamped event/application logs |
| IIS WebSocket | Confirm Windows/IIS WebSocket feature, open an authenticated Blazor page and inspect `/_blazor` | HTTP 101 WebSocket upgrade; no repeated negotiate failure | HAR/network screenshot with secrets/cookies redacted |
| SignalR reconnect | Briefly interrupt an approved staging browser connection, then restore it | Reconnect overlay appears once and clears/reloads safely; no duplicate mutation | Screen recording plus sanitized console/network log |
| Firewall/VPN | Test from approved and unapproved network locations | Approved app/VPN path works; public/unapproved SQL access is denied | Firewall rule reference and connection-test results without credentials |
| SQL TLS | Capture SqlClient/SQL Server negotiated protocol after certificate rollout | TLS 1.2 or later, trusted certificate, no trust bypass/downgrade warning | Sanitized client log and SQL/network configuration evidence |
| Authenticated matrix | Supply external storage states for approved full and restricted synthetic accounts, then run Playwright | Authenticated read routes and permission denial pass; no production account/data used | Playwright report showing executed—not skipped—cases |
| Cleanup | Run only against the approved isolated database/reset fixture | All prefixed test rows removed in dependency-safe order | Before/after row-ID manifest and cleanup approval |

## Readiness

Ready for server staging: **NO** until an authorized account/storage state exists and the SQL TLS warning is remediated or explicitly accepted for staging.
Ready for production cutover: **NO**.
