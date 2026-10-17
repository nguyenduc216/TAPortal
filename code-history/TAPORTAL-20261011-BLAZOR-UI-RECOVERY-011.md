# TAPORTAL-20261011-BLAZOR-UI-RECOVERY-011

- **FEATURE_ID:** `TAPORTAL-PLATFORM-BLAZOR-MIGRATION`
- **Date:** 2026-10-11
- **Scope:** Runtime acceptance follow-up, shared recovery UI, dirty-form guard, and portal shell polish.

## Changes

- Removed child-count badges from Blazor navigation and retained the existing MVC navigation tree/route behavior.
- Simplified the MVC topbar account trigger to show only the bold display name; role details remain in the account dropdown.
- Added the disabled, explicitly phase-limited customer action affordance to the Blazor customer page and aligned both customer empty states with the users/group icon.
- Added delegated `input`/`change` dirty-form detection to the shared recovery coordinator. Search fields are excluded, submit clears the form marker, and no form data is persisted or replayed.

## Validation

- Release build: passed, 0 warnings, 0 errors.
- Anonymous Playwright local runtime: 7 passed at `http://127.0.0.1:5194`; screenshots and reports remain under ignored test artifacts.
- Authenticated SignalR, controlled Kestrel stop/restart, maintenance interception, and dirty-form browser scenarios require an approved authenticated session or dedicated test harness and remain unverified in this change.

## Safety

- No production database writes, migrations, credentials, appsettings changes, or production deployment performed.