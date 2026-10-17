# CRM Phase1-011D authorization verification

- FEATURE_ID: `TAPORTAL-CRM-CUSTOMER-360`
- CHANGE_ID: `TAPORTAL-20261015-CRM-PHASE1-011D`
- Status: PARTIAL / BLOCKED. No isolated database credentials found in the
  inspected environment. Do not execute verification against production without
  owner review. `database/scripts/verify/011D-verify-crm-authorization-readonly.sql`
  is an SSMS-only, read-only metadata/action-mapping handoff for TAPortalSys.
- 011C query: outer EXISTS joins customer, active user, employee and status;
  requires one nondeleted employee and one valid primary/current HR assignment.
  Nested EXISTS checks active action mapping to a non-denied permission granted
  to user, active role or SYS_ADMIN. Separate OR checks SYS_ADMIN/user/role
  function-level Customers scope; NOT EXISTS subtracts matching User DENY.
  ASSIGNED uses current employee relation; TEAM uses a current primary team
  assignment; BRANCH uses owner/share; COMPANY uses owner branch. Relations
  use Bangkok current date with inclusive end dates. All inputs parameterized.
- Verified source defect: 011C only matched EndpointKey whereas public action
  API accepts UiKey OR EndpointKey. Minimal fix applies both keys. No other
  unverified SQL behavior was changed. In particular TEAM MAX(Id) selection
  and transaction-duration RBAC/HR effects remain unverified.
- Static checks are NOT SQL execution: SQL2014 syntax, actual schema, real grant
  fixtures, authorization success/denial and concurrency all require execution.
  No live SQL metadata was inspected. No writers enabled, VIEW untouched.
- Git isolation unsafe due unrelated dirty files and staged appsettings.json
  deletion. No branch/commit/push/PR/merge or SQL data writes.