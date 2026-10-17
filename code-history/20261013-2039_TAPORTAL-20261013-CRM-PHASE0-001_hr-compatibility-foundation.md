# CRM Phase 0 — HR compatibility foundation

- Date: 2026-10-13 20:39 Asia/Bangkok
- FEATURE_ID: `TAPORTAL-CRM-CUSTOMER-360`
- CHANGE_ID: `TAPORTAL-20261013-CRM-PHASE0-001`
- Decisions: approved `1A–18A` from the planning conversation.
- STATUS: **BLOCKED** — implementation and automated checks pass; isolated database-backed acceptance cannot run.
- Branch: `feat/compact-dynamic-navigation`
- HEAD before/after: `1dbad77668943fd43ccb3508dd152ce150748f88`
- Commit/push: none.

## Baseline and preservation

No AGENTS.md was found in the workspace ancestor chain or surveyed source/tests/database/docs/code-history trees. The source/history survey from the preceding planning task was retained and the current HR source, tests, schema and latest ADMIN-RBAC release history were rechecked. Release baseline build passed (0 warnings/errors); baseline full tests passed 89/89.

The repository already had overlapping Customers, RBAC, recovery, interface, tests and history changes, plus staged removal of the physical Web appsettings.json from the Git index. These changes were preserved. No reset, stash, staging, commit, push, configuration edit, migration, production mutation, publish or IIS deployment occurred. Current source is a dirty working-tree baseline, not an assertion that HEAD contains all verified code.

## Schema evidence and changes

Read-only inspection used the configured TAPortalSys connection. SQL Server version 12.0.4100.1 and compatibility 120 were confirmed. Departments and Teams require BranchId without defaults; Teams lacks Description and allows nullable DepartmentId. EmployeeAssignments has dated primary/current organization references; Employees links to Users and has employment status/date/active/deleted state. EmployeeStatuses contains ACTIVE, PROBATION, MATERNITY, SUSPENDED, RESIGNED and TERMINATED; all have catalog IsActive=1, so catalog availability must not be treated as employment eligibility. No employee/customer identity or password values were printed.

1. New Department creation selects an active Branch whose Company is active. Typed invalid-branch feedback handles missing/invalid selection. Existing Department branch ownership remains immutable on edit. Compatibility supports the earlier no-BranchId Department schema as well.
2. Team create/update derives BranchId from the validated Department in the serializable transaction. Existing parent/cycle/rowversion/dependency protection remains. Moving a Team to another Department is blocked when nondeleted child teams or any historical EmployeeAssignments reference it. Global Team Code uniqueness remains the application contract and is checked inside the serializable transaction even though the live unique index is branch-scoped.
3. Team SQL/UI adapts to absence of Description: read projects NULL, write omits the unavailable column, UI does not offer the field. An overposted nonblank Description is rejected with UnsupportedDescription instead of silently losing data. Nullable DepartmentId rows are included in the list, represented as unassigned, and excluded from parent options until assigned to a Department.
4. PortalDb remains the SQL owner, with a new HR compatibility partial. No ORM, second repository or production schema migration was introduced.
5. Employee access context is read afresh by authenticated user ID. ACTIVE/PROBATION/MATERNITY, employee active/not-deleted and employment dates define eligibility. RESIGNED/TERMINATED/SUSPENDED/unknown/null status, inactive/deleted, not-yet-started and ended employees fail CRM eligibility. EndDate and ValidTo are inclusive Asia/Bangkok business dates.
6. Current organization requires one primary/current assignment effective today, with consistent active Company/Branch/Department/Team relationships. Future/expired/invalid/ambiguous assignments yield no organization scope. There is no fallback to UserTeams/UserBranches or cookie role snapshots. Old organization assignments are not edited or deleted by resolution.
7. Direct CRM permission checks and effective-permission projections remove CRM grants for ineligible linked employees, including SYS_ADMIN permissions. Existing action/menu resolution consumes that projection. Non-CRM RBAC and non-employee portal accounts retain their existing semantics. The active-account check remains mandatory. No employee account, role, assignment or handover record was changed.

## Scope boundary and remaining limitations

CurrentOrganization is the HR input for later CRM scope enforcement. Phase 0 does not introduce customer ownership/sharing, customer-to-employee assignments or a handover workflow. The existing GetCustomersAsync query still lists all nondeleted Customers for a function-authorized user; per-customer transfer isolation is not claimed. Phase 1 must consume the current HR context in all customer read/export/write paths after the required CRM relations are added. Already rendered circuit content is not erased asynchronously by this change; fresh operations recheck authorization.

Linked employees with unknown/null employment status are deliberately denied CRM; this requires owner validation on the test database before rollout. Department branch changes are not a new supported workflow. Global Team Code behavior is retained instead of silently changing to per-branch codes. Database transaction behavior/concurrent SQL writes and authenticated MVC/Blazor acceptance remain unverified until the test environment is provisioned.

## Database test status

**TAPortalSys_CRM_Test: BLOCKED**.

- The configured connection targets TAPortalSys, not an approved separate test instance.
- CREATE ANY DATABASE=0 and sysadmin=0.
- DB_ID for the proposed test database returned NULL; restricted visibility means this is not proof of nonexistence.
- No approved isolated target identity was supplied; no database creation was attempted and no production fallback was used.
- No real data/hash/credentials were copied, no seed was executed, and no migration was run.

Provisioning and synthetic-data acceptance steps are in `docs/audits/CRM_PHASE0_TEST_DATABASE_RUNBOOK.md`. A DBA must designate/verify an isolated instance, provision the test-only database/principal, apply a reviewed schema-only export of the current target schema, then seed fabricated data. Legacy migrations 001–052 must not be replayed and reserved migration 054 must not be invented.

## Verification

- Baseline Release build: PASS, 0 warnings/errors.
- Baseline full suite: PASS, 89/89.
- Final Release build: PASS, 0 warnings/errors.
- Final full suite: PASS, 103/103, 0 failures, 0 skips.
- Added 14 test cases for employment status/date gates, transfer/history retention, expired/future/invalid/overlapping assignments, duplicate employee links, non-employee compatibility, actual Blazor branch binding/feedback and omitted Team Description controls. Fixtures are synthetic and SQL-free.
- Existing authentication/password, authorization/menu, Customers, HR hierarchy/concurrency contract, Blazor and recovery tests remain passing.
- New employee-context SELECTs validated via CommandBehavior.SchemaOnly against TAPortalSys: PASS, result schemas 1/6/10 columns. Adapted Team list schema: PASS, 15 columns. No production record values or mutations were used.
- git diff --check: PASS, pre-existing/configured LF/CRLF normalization warnings only.
- Database-backed write/concurrency and authenticated browser acceptance: BLOCKED, not represented as PASS.

## Files changed by this task

- `src/TAPortal.Web/Models/HrCompatibilityModels.cs` (new)
- `src/TAPortal.Web/Models/DepartmentModels.cs`
- `src/TAPortal.Web/Models/TeamModels.cs`
- `src/TAPortal.Web/Services/PortalDb.cs` (preserves prior uncommitted Customers/RBAC changes)
- `src/TAPortal.Web/Services/PortalDb.HrCompatibility.cs` (new)
- `src/TAPortal.Web/Services/IBlazorPortalService.cs` (preserves prior changes)
- `src/TAPortal.Web/Controllers/DepartmentsController.cs`
- `src/TAPortal.Web/Controllers/TeamsController.cs`
- `src/TAPortal.Web/Components/Pages/HrDepartments.razor`
- `src/TAPortal.Web/Components/Pages/HrTeams.razor`
- `src/TAPortal.Web/Views/Departments/Index.cshtml`
- `src/TAPortal.Web/Views/Departments/_DepartmentFields.cshtml`
- `src/TAPortal.Web/Views/Teams/_TeamFields.cshtml`
- `tests/TAPortal.Web.Tests/Phase8BlazorParityTests.cs` (test double extension; preserves prior changes)
- `tests/TAPortal.Web.Tests/HrPhase0Tests.cs` (new)
- `docs/audits/CRM_PHASE0_TEST_DATABASE_RUNBOOK.md` (new)
- This history entry (new).

## Readiness for CRM Phase 1

Not fully ready: designate and provision the isolated test database, run SQL write/concurrency and authenticated HR/RBAC/Customers acceptance with synthetic identities, and reconcile the overlapping uncommitted baseline under owner-controlled Git scope. Complete the customer ownership/scope contract in Phase 1; no Phase 1 implementation was started. No commit SHA was created and HEAD is unchanged.
