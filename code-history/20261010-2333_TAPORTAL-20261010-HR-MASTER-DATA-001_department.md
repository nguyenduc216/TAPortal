# TAPORTAL-20261010-HR-MASTER-DATA-001 — Department baseline

- Date: 2026-10-10
- Agent: Codex
- Project: T.A Portal
- Branch: `feat/compact-dynamic-navigation`
- FEATURE_ID: `TAPORTAL-HR-MASTER-DATA`
- CHANGE_ID: `TAPORTAL-20261010-HR-MASTER-DATA-001`
- Starting SHA: `d9568824d3c0f830936027d1a300cc41e933dde9`

## Request

Start Phase 7 with a production-ready Department master-data vertical slice that becomes the reusable CRUD/SQL/UI baseline for later HR masters, without implementing Employee, Team, JobTitle, or Specialization.

## Scope

Department table contract, normalized unique code, audit fields, rowversion concurrency, dependency-aware soft delete, dynamic permissions/actions/menu, SYS_ADMIN role grants, MVC CRUD, permission-aware UI, tests, migration governance, verification, build, publish, and Git delivery.

## Out of scope

Employee and other HR masters, new repository/ORM layers, authorization redesign, hard-coded role bypass, physical deletion, server pagination, and destructive legacy migration cleanup.

## Architecture reused

`PortalDb` remains the sole Portal SQL owner. Existing Phase 6A `ACTION:` authorization and database-filtered menu tree are reused. Phase 6B page header, compact tables, modals, icons, responsiveness, loading behavior, and reduced-motion rules are reused.

## SQL and migration

- Added `055-hr-department-master.sql`, guarded for `TAPortalSys`, transactional, additive, restart-safe, and SQL Server 2014 compatible.
- Creates or safely extends a compatible `dbo.Departments`, validates key types, rejects duplicate nondeleted codes, and creates a filtered unique index.
- Reconciles HR module, DEPARTMENTS function, four permissions, four FunctionActions/mappings, HR/Department menu nodes, VIEW MenuPermission, and four SYS_ADMIN RolePermissions.
- Added read-only `055-verify-hr-department.sql` with thirteen PASS/WARN/FAIL invariants and nonzero failure behavior.

## Migration numbering and hygiene

Repository inspection found migrations 001–053 only and no `054-seed-core-navigation.sql` or equivalent artifact. Operational context says 054 may have been executed manually, but its exact verified contents were unavailable. Number 054 remains reserved; creating different content under that number would be unsafe. Added `database/legacy-migrations/README.md` to document that 001–052 target legacy `TAPortal` and must not run against `TAPortalSys`, plus the recovery/hash follow-up for 054.

## Functions changed

- Department code normalization and RowVersion decoding.
- Department list and duplicate-code lookup.
- Parameterized create and optimistic update.
- Optimistic active/inactive transition.
- Transactional, reference-aware soft delete.
- Controller validation, capability projection, mutation result mapping, and audit-user propagation.

## Permissions, actions, and menu

Permissions: `HR.DEPARTMENT.VIEW`, `CREATE`, `UPDATE`, `DELETE`. Endpoint action keys: `Departments.Index`, `Create`, `Update`, `Delete`. Toggle status uses `Departments.Update`. Department menu `/hr/departments` is nested under HR and protected by VIEW.

## Files changed

Department models/controller/views; `PortalDb`; application CSS; migration 055; verify 055; legacy migration marker; authorization/SQL/rule tests; feature history; and this change history.

## Tests and static verification

- Release build: PASS, 0 warnings, 0 errors.
- Automated tests: PASS, 30/30.
- SQL Server 2014 `PARSEONLY`: migration PASS, verify PASS.
- Executing migration 055 against legacy `TAPortal` stopped at the database guard with error 52200 and exit 1 before mutation.
- `git diff --check`: PASS.

## Migration execution and verify status

BLOCKED on `TAPortalSys`. Windows integrated login failed with SQL errors 4060/18456 before database access. The configured application connection opens legacy `TAPortal`, which has no Departments or HR menu and was not mutated. Migration 055 and its verify script were therefore not executed against `TAPortalSys`; no runtime CRUD smoke is claimed.

## Build and publish

`dotnet publish src/TAPortal.Web/TAPortal.Web.csproj -c Release --no-build -o publish-TAPortal`: PASS. Web DLL, deps/runtime configuration, and application CSS were verified. Razor views are compiled into the application assembly, so a physical publish `Views` folder is not expected.

## Git

Final commit/push SHA is reported in the delivery response.

## Deployment notes

An authorized deployment identity must compare the target schema, recover/record exact migration 054 evidence, execute 055 against `TAPortalSys` using `sqlcmd -b -I`, run verify 055, then perform permission-specific Department CRUD/concurrency/dependency smoke tests. Do not run migrations 001–052 against TAPortalSys.
