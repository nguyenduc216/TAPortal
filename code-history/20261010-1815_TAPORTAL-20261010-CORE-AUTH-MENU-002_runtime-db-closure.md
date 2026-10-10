# TAPORTAL-20261010-CORE-AUTH-MENU-002 — Runtime/database closure

- Date: 2026-10-10
- Agent: Codex
- Project: T.A Portal
- Branch: `feat/compact-dynamic-navigation`
- FEATURE_ID: `TAPORTAL-CORE-AUTH-MENU`
- CHANGE_ID: `TAPORTAL-20261010-CORE-AUTH-MENU-002`

## Request

Complete the Phase 6A runtime/database closure while preserving the authorization architecture introduced by CHANGE 001.

## Scope

- Harden migration 053 for pre-existing compatible FunctionAction tables.
- Add a strict `TAPortalSys` guard, schema/FK/index preflight, conflict-aware seed, and postflight.
- Add a read-only text verification script.
- Add focused runtime-semantics and SQL artifact tests.
- Build, test, publish, commit, and push.

## Out of scope

Phase 6B UI, Employee/HR CRUD, Task, authorization redesign, permission caching, historical migrations 001-052, and destructive database operations.

## Architecture reused

`PortalDb` remains the single database owner. Request-time permission resolution, `ACTION:` policies, fail-closed unknown actions, database-driven menu filtering, and separate DataScope semantics remain unchanged.

## SQL changes

- Migration 053 now fails before mutation unless `DB_NAME()=N'TAPortalSys'`.
- Existing compatible tables are retained; required columns/types are validated.
- Missing trusted FKs and equivalent unique indexes are added only after orphan/duplicate checks.
- Five expected mappings are inserted idempotently; blank keys are filled, while conflicting nonblank keys fail.
- All work is transactional and followed by invariant checks.
- Added `database/scripts/verify/053-verify-core-dynamic-action-authorization.sql`.

## DB verification

The configured SQL credential opens only `TAPortal`. That legacy database has compatible FunctionAction columns and FKs, but it is not evidence for `TAPortalSys`. SQL Server 2014 PARSEONLY passed for migration and verify artifacts. Executing migration 053 against the wrong configured database stopped at the database guard before mutation. Running the verify script there produced text-only diagnostics, an overall FAIL, and a nonzero exit as designed.

## Migration execution status

BLOCKED. SQL login error: `Cannot open database "TAPortalSys" requested by the login. The login failed.` Windows Integrated access also failed before opening the database. Migration 053 was not executed on `TAPortalSys`.

## Runtime smoke result

BLOCKED because neither the application nor test identity can connect to `TAPortalSys`. No user permission assignments were changed.

## Files changed

Migration 053, TAPortalSys deployment notes, verify script, PortalDb authorization/menu projection, shared authorization semantics, focused tests, feature history, and this change history.

## Build, tests, publish, and Git

- `dotnet build TAPortal.sln -c Release --no-restore`: PASS, 0 warnings, 0 errors.
- `dotnet test TAPortal.sln -c Release --no-build`: PASS, 18/18.
- `dotnet publish src/TAPortal.Web/TAPortal.Web.csproj -c Release --no-build -o publish-TAPortal`: PASS; DLL, deps/runtime config, and `wwwroot` verified.
- `git diff --check`: PASS.
- Commit/push SHA is recorded in the final delivery report.

## Deployment blocker

An authorized `TAPortalSys` connection is required to inspect the exact target metadata, execute migration 053, run the verify script, and perform HTTP authorization smoke tests.
