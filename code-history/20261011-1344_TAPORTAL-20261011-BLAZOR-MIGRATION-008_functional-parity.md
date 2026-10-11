# TAPORTAL-20261011-BLAZOR-MIGRATION-008 — Functional parity

- Timestamp: 2026-10-11 13:44 Asia/Bangkok
- Feature: `TAPORTAL-PLATFORM-BLAZOR-MIGRATION`
- Starting SHA: `9a1d7e32a43b2b34992c79b32ed253acd125d201`
- Scope: Blazor trial routes only; MVC route ownership retained.

## Implemented

- Added Department edit UI using the existing update model, validation, function-action authorization, row version, typed result handling, duplicate-submit guard, loading state and post-success refresh.
- Added Team edit and Parent Team selection. Options are filtered by Department and exclude the edited Team; `PortalDb` continues to enforce active Department, same-Department parent, self-parent, cycle, concurrency and dependency rules transactionally.
- Added explicit loading/denied/empty/success/error/retry states for Roles and Menus with sanitized messages.
- Introduced `IBlazorPortalService` as a narrow component boundary; production DI resolves it to the existing `PortalDb`, so SQL ownership and behavior remain unchanged.
- Added bUnit interaction tests with an in-memory fake service; tests never connect to SQL.
- Extended credential-free Playwright scenarios and added the Phase 8 staging runbook/security tracking.

## Verification

- Baseline before changes: 62/62 tests PASS.
- Debug web build: PASS, 0 warnings, 0 errors.
- Phase 8 automated suite during implementation: PASS (final count recorded in delivery report).
- No database schema, migration, authentication cookie, password service, permission precedence, MVC production route, connection string, or production environment was changed.

## Runtime status

Authenticated Playwright, approved write tests, IIS WebSocket/SignalR interruption, reconnect recovery, SQL TLS remediation, test-account provisioning and cleanup evidence require authorized staging infrastructure. Skipped browser cases are not counted as PASS.

Status: **PARTIAL — Functional parity implementation complete, authenticated runtime pending**.
Ready for cutover: **NO**.
