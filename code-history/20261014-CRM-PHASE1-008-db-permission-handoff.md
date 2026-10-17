# CRM Phase1-008 database permission handoff

- FEATURE_ID: `TAPORTAL-CRM-CUSTOMER-360`
- CHANGE_ID: `TAPORTAL-20261014-CRM-PHASE1-008-DB`
- Status: `READY_FOR_MANUAL_SQL`
- Version policy: codec v2.3 STANDARD; SQL Server 2014, `TAPortalSys` only.

## Scope

Completed the manual SQL handoff for the CRM customer administration metadata:

- `CRM.CUSTOMERS.ASSIGN` -> `Customers.Assign`
- `CRM.CUSTOMERS.SHARE` -> `Customers.Share`
- `CRM.CUSTOMERS.TEAM` -> `Customers.Team`

Each permission is linked to an active `FunctionAction` and the required
`FunctionActionPermissions` row. FunctionAction keys follow the existing
dynamic authorization contract:

- `UiKey`: `customers.assign`, `customers.share`, `customers.team`
- `EndpointKey`: `Customers.Assign`, `Customers.Share`, `Customers.Team`

## Safety and execution

- `database/seeds/crm/008-seed-customer-admin-permissions.sql` is manual-only,
  transactional, idempotent, SQL Server 2014 compatible, and does not use hard-
  coded GUIDs.
- It does not insert into `UserRoles`, `RolePermissions`, or
  `UserPermissions`; no user or role receives administrative access implicitly.
- `database/scripts/verify/008-verify-customer-admin-permissions.sql` is
  read-only, emits every PASS/FAIL message through `RAISERROR ... WITH NOWAIT`,
  and fails when the metadata/mapping contract is incomplete.
- No SQL was executed by Codex. No DDL migration was added.
- Existing permission metadata is never updated: inactive/deleted or conflicting
  records, action keys, and unexpected mappings stop the transaction for manual
  review. Source checks use `HasFunctionActionAsync`; user DENY precedes the
  validated SYS_ADMIN bypass. `Customers.View` is not mapped to these actions.

## Manual run order

1. Confirm the applicable core authorization migration (including migration
   053) is already deployed in `TAPortalSys`.
2. Run `database/seeds/crm/008-seed-customer-admin-permissions.sql` in SSMS.
3. Run `database/scripts/verify/008-verify-customer-admin-permissions.sql` in
   SSMS and retain the PASS/FAIL output.
4. An administrator may then explicitly assign the three permissions through
   the approved role/user administration process. Review existing grants and
   denies before doing so; do not grant all users by default.

## Verification expectations

- Seed: `[PASS] 008 CRM customer administration permissions and action mappings registered.`
- Verification: overall `VerificationStatus=PASS | Passed=N | Failed=0`, with three permission/action mapping
  PASS messages and no FAIL messages.
- Role/user counts are informational only. A zero count is acceptable and is
  the fail-closed default; the seed does not create those assignments.

## Validation in repository

- SQL Server execution: `NOT RUN` by Codex, per handoff requirement.
- Application build/tests: no application code changed for this handoff;
  existing release build/tests remain the prior validation baseline.
- Git: existing dirty tree and staged configuration deletion prevent safe branch
  isolation; no staging, commit, push or PR attempted. Human review required.
- Publish: NOT_APPLICABLE; SQL/documentation only, no application artifact.