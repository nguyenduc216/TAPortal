# CRM Phase 1 customer scope SQL integration

- FEATURE_ID: `TAPORTAL-CRM-CUSTOMER-360`
- CHANGE_ID: `TAPORTAL-20261014-CRM-PHASE1-008`
- Status: `PARTIAL_PENDING_VALIDATION`

Customer list and MVC export now share a parameterized `PortalDb` query. The
query gates `Customers.Index`, validates the active account, preserves the
validated SYS_ADMIN bypass, and evaluates explicit user/role DataScopes for
ASSIGNED, TEAM, BRANCH and COMPANY. User DENY and null references fail closed.
Search is applied in SQL, so export does not load an unscoped customer set.

An idempotent, manual-only permission metadata seed was added for
`Customers.Assign`, `Customers.Share`, and `Customers.Team`; it was not run.
No migration, database execution, UI, update/revoke workflow, or deployment was
performed. Integration tests remain NOT_RUN pending database connectivity.

## Files

- `src/TAPortal.Web/Services/PortalDb.cs` — single parameterized, database-side
  scope predicate; DB-side search; `ScopeReferenceId IS NULL` fails closed;
  explicit user DENY subtracted in SQL; SYS_ADMIN bypass after account/function
  gates.
- `src/TAPortal.Web/Services/IBlazorPortalService.cs` — search overload added.
- `src/TAPortal.Web/Controllers/PortalControllers.cs` — DB-side search plus a
  final `CustomerRules.Filter` compatibility pass for existing test doubles.
- `database/seeds/crm/008-seed-customer-admin-permissions.sql` — manual-only,
  idempotent, transactional MERGE seed; key-based, no GUIDs, no role/user grant.
- `tests/TAPortal.Web.Tests/CrmScopeSqlContractTests.cs` — locks the scope SQL
  contract (gates, four scope types, fail-closed, DENY precedence, full
  parameterization) and the seed safety contract.

## Verification

- Release build: PASS (0 warnings, 0 errors).
- Unit tests: PASS — 112 passed, 0 failed (6 new scope/seed contract tests).
- Integration tests: `NOT_RUN` — no SQL Server connectivity in this environment;
  the DB-backed per-branch matrix is still outstanding.

## Outstanding (owner / DB admin)

1. Run `database/migrations/crm/00*`, then review and execute
   `01_customer_scope_audit.sql`, then `03_verify_customer_scope_audit.sql`.
2. Run `database/seeds/crm/008-seed-customer-admin-permissions.sql` manually.
3. Add DB-backed integration tests per scope branch (ASSIGNED/TEAM/BRANCH/
   COMPANY, DENY, role/user ALLOW, SYS_ADMIN, transfer, terminated/suspended,
   expired, no-permission, out-of-scope export).