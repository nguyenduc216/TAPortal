# TAPORTAL-20261011-BLAZOR-UI-RECOVERY-012

- **FEATURE_ID:** `TAPORTAL-PLATFORM-BLAZOR-MIGRATION`
- **Scope:** Dirty-state correctness, controlled local recovery E2E, shared MVC/Blazor overlay styling, and staging package refresh.

## Implementation

- Removed the submit-event dirty marker clear. Validation failures, save failures, network errors, and timeouts therefore retain `data-ta-dirty="true"`.
- Added explicit `TAPortalRecovery.clearDirty(form)` and reset-event handling. Components may call it only after a confirmed successful save; reset is an intentional clear operation.
- Added recovery coordinator regression tests for dirty transitions, maintenance/restart/recovered states, responsive geometry, reduced-motion CSS presence, offline/online events, and singleton host behavior.
- Added a controlled Kestrel process test that stops the exact process tree, keeps Chromium open, verifies the Vietnamese overlay and preserved login DOM, restarts the same port, and verifies recovery.
- Added recovery overlay CSS to the shared MVC stylesheet. This fixes MVC pages that do not load the Blazor-only stylesheet.

## Validation

- Source `appsettings.json` exists physically but is removed from the Git index; it was not restored or committed.
- Publish `appsettings.json` was preserved byte-for-byte during republish.
- Release build: 0 warnings, 0 errors.
- .NET automated tests: 76/76 passed.
- Recovery visual/dirty Playwright: 3/3 passed.
- Controlled Kestrel stop/restart Playwright: 1/1 passed.
- Authenticated SignalR and IIS recycle/WebSocket validation remain staging-only evidence.

## Safety

- No database writes, migrations, credentials, production deployment, or secret logging performed.