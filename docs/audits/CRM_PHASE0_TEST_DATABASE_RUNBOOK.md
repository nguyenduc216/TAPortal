# CRM development database baseline

## Current owner decision — 2026-10-13

- FEATURE_ID: `TAPORTAL-CRM-CUSTOMER-360`
- CHANGE_ID: `TAPORTAL-20261013-CRM-DB-BASELINE-001`
- Approved development/integration database: **`TAPortalSys`**.
- A separate test database/instance is no longer required. This decision supersedes the earlier provisioning requirement below and the corresponding blocker in Phase 0 history.
- Real customer, employee, account and password records must not be used or modified as test fixtures.
- Schema changes require an identified target, reviewed preflight, verified recoverable backup and a rollback plan. Legacy migration chains must not be replayed.

## Current backup gate

The configured connection was rechecked: DB_NAME is TAPortalSys; configured endpoint was reused without substitution; SQL server-name metadata is internally consistent. Server version is 12.0.4100.1. No server identity or connection secrets are printed in this document. An instance name alone is not backup/restore evidence.

The principal reports BACKUP DATABASE=1, ALTER=1, CREATE TABLE=1, sysadmin=0. No disk backup device is registered. No full backup for TAPortalSys is present in the accessible msdb backup history. No approved backup path is configured in the inspected application settings, deployment files or backup/restore environment variables. These findings do not prove there are no external/offline backups; none has been verified for this task.

**BACKUP: BLOCKED.** No backup destination was invented, no existing backup was overwritten, and no migration or synthetic DML was executed after this gate failed. Transaction rollback alone is not a substitute for the recoverable backup requested by the owner.

## Steps to unblock on the approved development database

1. The owner/DBA must configure a server-local directory or approved backup device on the currently configured SQL instance. It must be writable by the SQL Server service account, access-restricted, outside web/public folders and suitable for sensitive database contents. A workstation path is not automatically a valid remote SQL backup path.
2. Supply the destination through secure operational configuration. Confirm the intended server identity through the configured endpoint and SQL metadata before using it. If a backup already exists, provide its verification/restore evidence through the same operational channel.
3. Before DDL, create a uniquely named COPY_ONLY full backup with CHECKSUM, without FORMAT/INIT or overwriting another backup. Verify the backup header matches TAPortalSys and the expected instance; run RESTORE VERIFYONLY with CHECKSUM and retain evidence. Identify a DBA able to restore it and confirm a feasible recovery destination/procedure. VERIFYONLY is not a restore rehearsal; record this limitation and require a rehearsal when recoverability remains uncertain.
4. Review additive Phase 1 schema and existing data, including customer ownership/backfill. Do not assign existing customers arbitrarily to a branch. Require explicit ownership reconciliation before enforcing NOT NULL on old rows.
5. Execute only the reviewed necessary migration, guarded by DB_NAME and confirmed instance identity, in a transaction with pre/postconditions. Keep an application rollback strategy that preserves added data; no DROP/truncation rollback scripts.
6. Use uniquely tagged generated fixture rows and new test-only credentials. Prefer transaction rollback for same-connection tests; for HTTP/service multi-connection acceptance, maintain an exact created-ID manifest and cleanup only those test-owned rows in FK order. Never select real rows as fixture parents or delete by broad prefix alone. If cleanup fails, report the retained IDs through a protected artifact without exposing credentials.
7. Repeat HR/authentication/RBAC acceptance and Phase 1 URL/query/data-scope acceptance; verify no test fixture remains unintentionally.

## Previous provisioning plan — historical, superseded

The following section records the earlier Phase 0 plan. Its separate-instance/database requirements no longer apply. Its original permission findings are historical and are not substituted for the current backup/DDL checks above.

# CRM Phase 0 test database provisioning (historical)

- FEATURE_ID: `TAPORTAL-CRM-CUSTOMER-360`
- CHANGE_ID: `TAPORTAL-20261013-CRM-PHASE0-001`
- Requested database: `TAPortalSys_CRM_Test`
- Status: **BLOCKED — no approved isolated SQL instance and no database creation permission**.

## Inspection performed

The existing configured connection opened `TAPortalSys`. SQL Server reports version `12.0.4100.1`, compatibility level 120. `CREATE ANY DATABASE=0`, `sysadmin=0`; `DB_ID('TAPortalSys_CRM_Test')` returned NULL. NULL does not prove absence under restricted metadata visibility. No CREATE DATABASE, production DDL, migration or data mutation was attempted. Server/login/connection details are intentionally omitted.

## Required owner/DBA steps

1. Designate an approved development/test SQL instance separate from the production instance. A differently named database on an unverified production server is not sufficient isolation for this task.
2. Verify the instance identity, purpose and access restrictions with the owner before any provisioning. Supply the approved instance through secure configuration, not Git or chat. Use a supported compatible SQL instance; compatibility 120 alone is not full SQL Server 2014 equivalence.
3. Have a DBA inspect whether `TAPortalSys_CRM_Test` already exists. If absent, create the empty database on that approved instance with compatibility 120. Use a provisioning principal only for provisioning; give the runtime/test principal access only to the test database. Verify the test principal cannot open production.
4. Export the current 38-table `TAPortalSys` schema using metadata/schema-only scripting. Include columns, defaults, primary/foreign keys, unique/filtered indexes and check constraints. Do not include table data, logins, users, passwords, server objects, backup/restore copies or secret settings. Review the output for embedded values before use.
5. Retarget the reviewed schema script exclusively to `TAPortalSys_CRM_Test`, with a fail-before-mutation `DB_NAME()` guard and an exact approved test-instance guard. Apply only there. Do not replay legacy migrations 001–052; they target `TAPortal`, not this schema. Do not invent migration 054.
6. Retain the current HR schema variant for acceptance: Departments/Teams require BranchId; Teams has no Description; Teams.DepartmentId is nullable; current assignment rows have dates and organization references. The Phase 0 code adapts to this variant without adding a production column.
7. Populate synthetic companies, branches, departments, teams, employee statuses, employees, organizational assignments, users and RBAC/action metadata. Create new test-only passwords through PasswordService; do not import real hashes, users, employee records or customers. Use fabricated customer data only for existing Customers regression.
8. Keep the test connection in environment/secure settings, without altering the source production configuration. Start a local test runtime explicitly pointed at the test database; Bootstrap:EnsureAdmin stays disabled unless separately provisioned for a test-only purpose.

## Acceptance scenarios after provisioning

- Department create: required active branch/company, invalid/missing branch, duplicate code, concurrent create. Editing retains branch ownership.
- Team list/options: missing Description, null DepartmentId, visible existing rows; unsupported descriptions rejected rather than silently dropped.
- Team writes: branch derived from department; valid parent branch/department; self/cycle denial; global code uniqueness retained under concurrency; stale rowversion rejected.
- Moving a team used by child teams or any historical employee assignment is rejected; status/delete keep prior dependency behavior. No assignment/handover history is rewritten.
- Employee eligibility: ACTIVE/PROBATION/MATERNITY permitted, RESIGNED/TERMINATED/SUSPENDED/unknown/null denied. Inactive/deleted/future/ended employees denied for CRM. EndDate/ValidTo are inclusive local business dates (Asia/Bangkok).
- Transfer: ending the old primary/current assignment and starting the new one produces only the new organization; future/expired/inconsistent/ambiguous organizations grant no organization scope. No fallback to stale UserTeams/UserBranches or cookie claims.
- Existing non-employee portal users retain existing RBAC behavior. Disabled users still fail authorization. Test ordinary DENY, SYS_ADMIN, action mappings, protected menu filtering, Customers Create and export.
- Authenticate with test-only identities; exercise MVC and Blazor HR forms, permissions revoked after page load, rowversion conflicts, persistence and reconnect.

## Phase 1 boundary

Phase 0 supplies current HR organization and removes CRM permissions for ineligible linked employees. Customers still has no branch ownership/sharing or employee-to-customer relationship. Its existing list query is not row-scoped. Therefore this change does **not** claim that customer-by-customer transfer isolation or handover is complete. Phase 1 must introduce the approved CRM ownership/assignment relationships and consume the current organization in every list/detail/export/write path.

No automatic schema creation, seed execution or production fallback is included. Database-backed write/authenticated acceptance remains blocked until the prerequisites above are met.
