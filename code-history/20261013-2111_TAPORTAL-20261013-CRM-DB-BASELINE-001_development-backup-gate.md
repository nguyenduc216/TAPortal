# CRM development baseline and Phase 1 backup gate

- Date: 2026-10-13, Asia/Bangkok
- FEATURE_ID: `TAPORTAL-CRM-CUSTOMER-360`
- CHANGE_ID: `TAPORTAL-20261013-CRM-DB-BASELINE-001`
- STATUS: **BLOCKED**
- BRANCH: `feat/compact-dynamic-navigation`
- HEAD_SHA at start: `1dbad77668943fd43ccb3508dd152ce150748f88`
- PHASE0: **FAIL (acceptance incomplete; not an automated-test failure)**
- PHASE1: **BLOCKED**
- DATABASE: `TAPortalSys`
- BACKUP: **BLOCKED**
- MIGRATION: none created/executed
- BUILD: PASS, Release, 0 warnings/errors
- TESTS: PASS, 103/103, 0 skipped
- PLAYWRIGHT: PASS, 5/5 anonymous local Chromium cases; authenticated/fixture acceptance BLOCKED
- COMMIT_SHA: documentation-only commit recorded in final report
- PUSH: not performed; one predecessor commit was already ahead of the upstream at entry
- PUBLISH_PATH: none; no publish/IIS action

## Owner decision and baseline

The owner explicitly authorized development and synthetic integration testing directly on TAPortalSys. This replaces the separate test database/instance requirement from CRM Phase 0. Decisions 1A–17A and the substantive CRM design are retained; the separate environment requirement of 18A is superseded. This permission does not waive the owner's new backup/rollback gate.

No AGENTS.md was found in the surveyed project trees or workspace ancestor chain. The existing Phase 0 history and implementation were re-read. The dirty working tree includes pre-existing Customers/RBAC/recovery work, Phase 0 HR changes and histories, schema audit artifacts and staged appsettings.json index removal. No predecessor work was overwritten, reset, stashed or staged as source for this task. No connection string, password/hash or existing personal/customer data was printed.

Working-tree categories:

- HR Phase 0: HR compatibility models/partial, Department/Team models/controllers/components/views, interface extension, fake-service change and HR tests.
- Predecessor application changes: Customers/create/export, SYS_ADMIN effective permissions, loading/reconnect, Program/layout/style/scripts.
- Predecessor documentation/schema/test artifacts: histories, feature histories, legacy schema audits, review-only Customers metadata SQL, Customers harness and Playwright suites.
- Sensitive local configuration: staged removal of Web appsettings.json while its physical file is retained; no config staging or edits by this task.
- This task: updated CRM database runbook and this new history only. Browser reports are generated ignored artifacts.

The branch was already one commit ahead of its configured upstream (`test(blazor): verify phase 9 local runtime`). Publishing this branch would include that predecessor scope. A documentation-only local commit is kept separate; no push of the overlapping application baseline is performed.

## Read-only database preflight

The existing configured endpoint was reused with no server/database substitution. DB_NAME returned TAPortalSys; SERVERPROPERTY server-name metadata and @@SERVERNAME are consistent. Version is 12.0.4100.1. Instance identifiers are intentionally omitted from published documentation.

- 38 existing user tables; reviewed Customers/Users/Roles/Permissions and HR columns/defaults and relevant indexes.
- All foreign keys remain enabled/trusted (0 disabled/untrusted).
- Customers has filtered global unique Code and nonunique TaxCode index.
- Teams has branch-scoped Code index; application still enforces the pre-existing global Code contract.
- EmployeeAssignments has a unique filtered primary/current EmployeeId index.
- Aggregate invalid assignment date ranges: 0.
- Aggregate mismatched assignment Branch/Department/Team references checked: 0.
- Aggregate duplicate nondeleted Customer Code groups: 0.

These aggregate checks do not select real identities or use existing rows as synthetic fixtures. They are not a full audit of effective assignment overlap or Customer ownership, which does not exist yet.

## Backup and rollback result

The configured principal reports BACKUP DATABASE=1, ALTER=1, CREATE TABLE=1, sysadmin=0. Thus the remaining blocker is not absence of the basic backup/DDL permissions.

No disk backup device is registered. No full backup for TAPortalSys is present in the accessible msdb history. No approved backup directory/device configuration was found in inspected source settings, deployment files or backup/restore environment variables. This does not establish that an external backup does not exist; it establishes that this task has no verified recoverable backup or approved destination.

The owner explicitly required stopping database changes when backup/rollback cannot be verified. No destination was guessed and no BACKUP, RESTORE, CREATE/ALTER, migration, fixture INSERT/UPDATE/DELETE or other database mutation was issued. Existing data is untouched. Synthetic integration writes, transfer/retirement scenarios and authenticated acceptance are blocked by this gate even though they are otherwise authorized. No cleanup is necessary because no fixture was created.

## HR verification and Phase 1 boundary

Source review confirms Phase 0 branch-compatible Department/Team writes, handling of missing Team.Description, current organization resolution, inclusive local assignment/employment dates, CRM denial for retired/disabled linked employees, preserved nonemployee RBAC semantics and no cached UserTeams/UserBranches fallback.

Release build and existing 103 tests pass. This is code/automated evidence, not database-backed Phase 0 acceptance. Accordingly PHASE0 is recorded FAIL/incomplete in the mandatory PASS/FAIL field, with the environmental cause stated. No new HR defect is inferred from that classification.

Phase 1 was not implemented because the requested prerequisite (accepted Phase 0) and backup gate are unsatisfied. Current Customers service still lists all nondeleted customers once function-authorized; it is not row-scoped. Owner branch/sharing, CustomerEmployeeAssignments, organization scopes and transactionally audited grants remain Phase 1 work. No claim is made that transfer isolation or secret-view SYS_ADMIN policy is completed.

## Verification executed

- `dotnet build TAPortal.sln -c Release --no-restore`: PASS, 0 warnings/errors.
- `dotnet test TAPortal.sln -c Release --no-build --no-restore`: PASS, 103/103.
- Local Release Kestrel was launched on loopback with `Bootstrap:EnsureAdmin=false`; no IIS or publish action. Owned process shut down after testing.
- Playwright phase9 suite excluding the login-submission case: PASS, 5/5. Desktop/tablet/mobile login, protected URL anonymous redirects, branded 404 and browser error checks pass.
- The synthetic invalid-login submission case was deliberately not run after the database-write stop, rather than treating all authentication flows as proven read-only.
- Authenticated HR/RBAC/Customer scope SQL/API/browser tests: BLOCKED. Anonymous URL checks do not prove authorized row isolation.
- No DB fixtures, external messages, DDL, deployment or Phase 2+ work.

## Files changed and unblock instructions

1. `docs/audits/CRM_PHASE0_TEST_DATABASE_RUNBOOK.md`: current owner decision, current backup preflight, safe backup/restore and fixture-cleanup steps; former separate-instance plan retained explicitly as historical/superseded.
2. This history entry: baseline, working-tree categories, permission/preflight evidence, tests, failed backup gate and exact limits.

To resume: owner/DBA configures an approved server-side backup path/device on the currently configured instance, confirms service-account write permissions and restricted storage, and supplies existing verified backup/restore evidence or allows this connection to create a uniquely named COPY_ONLY full backup with CHECKSUM. Validate database/instance headers, RESTORE VERIFYONLY/CHECKSUM and the DBA restore procedure/recovery destination; perform a restore rehearsal if recoverability is uncertain. Then run synthetic Phase 0 acceptance, reconcile existing Customer ownership before adding required branch semantics, and execute only reviewed guarded additive Phase 1 SQL.

No separate test database is required anymore. No Phase 2 implementation was started.
