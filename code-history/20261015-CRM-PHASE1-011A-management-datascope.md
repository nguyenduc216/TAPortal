# CRM Phase1-011A management DataScope authorization

- FEATURE_ID: `TAPORTAL-CRM-CUSTOMER-360`
- CHANGE_ID: `TAPORTAL-20261015-CRM-PHASE1-011A`
- Status: BLOCKED. Architecture owner: `PortalDb` and HR/CRM partials.
- Candidate independent FunctionCodes: `Customers.Assign`, `Customers.Share`,
  `Customers.Team` (VIEW matches only `Customers` / `Customers.Index`). Not deployed.
- Contract gap: `database/migrations/005-create-data-scope.sql` defines
  `PK_RoleDataScopes(RoleId, DataScopeId, FunctionCode)` and
  `PK_UserDataScopes(UserId, DataScopeId, FunctionCode)` without ScopeReferenceId.
  A user cannot store ALLOW branch A and DENY branch B for one action/scope;
  a role cannot grant both A and B. TEAM/COMPANY have the same limitation.
  Duplicate DataScope identities or NULL wildcard are not approved workarounds.
- Existing `HasFunctionActionAsync` checks action metadata; UserPermissions DENY
  wins over active SYS_ADMIN. VIEW subtracts only matching UserDataScopes DENY.
  Legacy HR rules allow non-employees for reads: management must require
  HasEmployeeLink and eligible status, and valid organization where applicable.
- No service API, seed, DDL, database access, publish or relation writes.
  Create entry points remain Forbidden. DB-backed integration NOT_RUN.
- GIT_NEEDS_APPROVAL: unrelated dirty tree and staged appsettings deletion;
  no branch, staging, commit, push, PR or merge.
- Next: approve grant identity/schema contract for multiple targeted references
  and effects, then implement transaction-aware authorization and service tests.