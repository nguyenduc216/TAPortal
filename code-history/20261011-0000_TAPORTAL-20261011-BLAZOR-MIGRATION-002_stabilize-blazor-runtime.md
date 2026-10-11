# Stabilize Blazor Interactive Server Foundation

- FEATURE_ID: `TAPORTAL-PLATFORM-BLAZOR-MIGRATION`
- CHANGE_ID: `TAPORTAL-20261011-BLAZOR-MIGRATION-002`
- date: 2026-10-11
- agent: Codex
- branch: `feat/compact-dynamic-navigation`
- starting SHA: `4a644bcd92d7b7ede82860902404d3498facc923`

## Initial state and findings

Phase 1 had valid Interactive Server registration and a Blazor dashboard, but reconnect markup was static and not wired to the framework reconnect contract. Loading events called `StateHasChanged` directly, had no flicker delay, and operation scopes were not idempotent. Sidebar active-route comparison used an absolute URI. MVC routes were intentionally still retained.

## Scope implemented

- Kept cookie auth, PortalDb, OperationalDb, PasswordService, dynamic policies, database navigation, MVC routes, and database schema unchanged.
- Added framework-compatible reconnect modal host/classes, retry/reload actions, and reconnect JS bridge.
- Hardened scoped global loading with locking, safe messages, idempotent disposal, delayed overlay display, cancellation, and `InvokeAsync` UI dispatch.
- Corrected Sidebar active path handling.
- Added focused loading tests.
- Added `docs/audits/blazor-migration-phase2-route-inventory.md`.
- Updated feature history.

## Runtime and security verification

Build/test/source verification passed. Full authenticated runtime testing requires a configured SQL Server database and was not claimed. IIS recycle, browser network loss, cookie expiry, multi-tab, and SignalR reconnect scenarios were not browser-tested in this environment. No authentication or authorization bypass was introduced.

## Results

- `dotnet build TAPortal.sln -c Release --no-restore`: PASS, 0 warnings/errors
- `dotnet test TAPortal.sln -c Release --no-build`: PASS, 47/47
- Publish: PASS to non-production `publish-TAPortal-phase2`; DLL, runtimeconfig, web.config and application static assets present. Blazor Web App serves framework scripts through its endpoint rather than a physical `wwwroot/_framework/blazor.web.js` file.
- HTTP smoke: `/account/login` 200, invalid route 404. `/` was BLOCKED by missing local database configuration during dashboard prerender; no destructive operation was attempted.
- Database migration: NONE
- Production deployment/restart: not performed

## Remaining issues / next step

The MVC modules listed in the route inventory remain unchanged. The next change should migrate one route family at a time after authenticated browser parity testing. Reconnect behavior should be exercised with Kestrel process stop/restart or IIS recycle before acceptance. Browser network-loss, cookie-expiry, IIS recycle and multi-tab scenarios remain NOT TESTED.