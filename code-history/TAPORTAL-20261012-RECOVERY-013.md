# TAPORTAL-20261012-RECOVERY-013

- **FEATURE_ID:** `TAPORTAL-PLATFORM-BLAZOR-MIGRATION`
- **CHANGE_ID:** `TAPORTAL-20261012-RECOVERY-013`

## Root cause

The recovery coordinator's `poll()` returned immediately while `state === 'hidden'`, and initialization did not schedule an independent heartbeat. Healthy MVC pages therefore had no readiness probes unless a browser online event or Blazor lifecycle mutation occurred. IIS shutdown could leave the coordinator permanently hidden.

## Fix

- Start a readiness probe during coordinator initialization and schedule probes while hidden.
- Use a 7-second healthy interval with jitter, 4.5-second timeout, exponential failure backoff, and an overlap guard.
- Require HTTP success, JSON content, `ready === true`, and `maintenance !== true` for healthy status.
- Treat HTTP 404/5xx, HTML, invalid JSON, timeout, network failure, and `ready === false` as failures.
- Transition to recovery UI after two consecutive failures.
- Add non-sensitive `getDiagnostics()` output: scheduler status, probe timestamp, HTTP status, failure count, current state, next delay, and transition reason.
- Keep existing dirty-form, circuit lifecycle, singleton overlay, and safe reload behavior.

## Verification

- Controlled local Kestrel stop/restart: passed.
- Heartbeat/failure injection tests: passed.
- Anonymous responsive/browser regression: passed.
- .NET automated tests: 76/76 passed.
- Release build: 0 warnings, 0 errors.
- No production IIS restart/deploy, database writes, schema changes, authentication changes, or connection-string changes.