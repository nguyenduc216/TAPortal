# TAPORTAL-CRM-CUSTOMER-360

## Phase1-017 development integration

- CHANGE_ID: `TAPORTAL-20261015-CRM-PHASE1-017`; status PARTIAL. Local Kestrel and anonymous Playwright ran; authenticated SQL integration NOT_RUN because configured endpoint is non-local and no authorized isolated database or browser login state was supplied.
- Narrowed candidate selectors to active organization/posting conditions and corrected dialog pending-open cancellation. No permission policy, schema, grants or real customer data changed.
- Offline component and .NET test results are not SQL-backed evidence. See `code-history/20261015-CRM-PHASE1-017-development-integration.md`.

## Phase1-016 relationship UI

- CHANGE_ID: `TAPORTAL-20261015-CRM-PHASE1-016`; development implementation, SQL integration not verified.
- Added scoped customer relationship detail, three server-backed sections, target selectors and Create/Change/Revoke dialogs using existing PortalDb writers and persisted RowVersion tokens.
- In-memory bUnit component coverage added; browser and SQL authorization/transaction behavior still require isolated verification. No database migrations or IIS deployment.

## Phase1-008 database handoff

- CHANGE_ID: `TAPORTAL-20261014-CRM-PHASE1-008-DB`
- Status: `READY_FOR_MANUAL_SQL`; database verification `NOT_RUN`.
- Scope: manual seed and read-only verification of `CRM.CUSTOMERS.ASSIGN`,
  `CRM.CUSTOMERS.SHARE`, `CRM.CUSTOMERS.TEAM` under CRM / Customers.
- Files: `database/seeds/crm/008-seed-customer-admin-permissions.sql`,
  `database/scripts/verify/008-verify-customer-admin-permissions.sql`,
  `code-history/20261014-CRM-PHASE1-008-db-permission-handoff.md`.
- Assumptions: core authorization migrations including 053 already deployed;
  active CRM module and Customers function exist. No role/user grants are seeded.
- Execution order: confirm migrations and database selector `TAPortalSys`;
  execute seed; execute verification; save SSMS Messages; review grants separately.
- Risks: actual metadata and runtime permission behavior cannot be validated
  without owner-run SQL. Dirty working tree blocks safe branch/PR isolation.
- Publish: NOT_APPLICABLE (SQL-only); merge requires human review.

## Phase1-009 customer relations management

- CHANGE_ID: `TAPORTAL-20261015-CRM-PHASE1-009`.
- Status: PARTIAL / BLOCKED for the write workflow. The existing Phase1-007
  create path checked the action but did not check customer scope or target HR/
  organization eligibility in its transaction; its three entry points are now
  fail-closed. No create/change/revoke UI or database data writes were performed.
- Required before re-enabling: transactional actor/customer scope and target
  checks, RowVersion change/revoke, audit rollback and concurrency tests, and
  database-backed verification. Existing dirty tree prevents safe Git isolation.

## Phase1-010 secure relation workflows

- CHANGE_ID: `TAPORTAL-20261015-CRM-PHASE1-010`; status BLOCKED for writes.
- `PortalDb.GetCustomersAsync` implements a Customers.Index *view* scope using
  DataScopes for `Customers` / `Customers.Index`. Neither that projection nor
  `HasFunctionActionAsync` establishes a customer-specific management scope for
  Assign/Share/Team. The Phase1-009 create entry points remain fail-closed.
- Required policy decision: define which independently assigned DataScope
  FunctionCode(s) authorize management of each customer for each action,
  including DENY, SYS_ADMIN, owner-branch and cross-branch restrictions. Then
  implement and verify authorization under the same transaction and locks as
  target validation, RowVersion mutations and AuditLogs. No DDL or DB writes.

## Phase1-011A management scope

- CHANGE_ID: `TAPORTAL-20261015-CRM-PHASE1-011A`; BLOCKED by grant identity.
- RoleDataScopes and UserDataScopes primary keys omit ScopeReferenceId; a
  user cannot hold ALLOW branch A and DENY branch B for one action/scope type.
  Approval for multiple targeted reference/effect grants is required before
  implementing the management API. Create entry points remain Forbidden.

## Phase1-011B unified action and CRM scope

- CHANGE_ID: `TAPORTAL-20261015-CRM-PHASE1-011B`; status BLOCKED for release.
- Revised policy uses effective action permission AND existing function-level
  CRM customer scope; no separate management grants or schema changes. The
  earlier 011A grant-cardinality blocker does not itself require new schema
  under this policy, but the existing VIEW query cannot be used as a write
  authorization result. Its function-code matching is `Customers` and
  `Customers.Index`; it must not silently confer scope for a different action.
- No transaction-bound action/employee/scope routine is present yet. The three
  create entry points remain Forbidden; database integration and publish NOT_RUN.

## Phase1-011C transaction-bound foundation

- CHANGE_ID: `TAPORTAL-20261015-CRM-PHASE1-011C`; status PARTIAL pending
  database-backed authorization matrix and shared VIEW predicate refactor.
- Added an internal caller-transaction-only PortalDb action + employee +
  customer scope evaluator. It does not open a connection or enable writers.
  Existing customer list/export query remains unchanged; no schema or grants.

## Phase1-011D transaction authorization verification

- CHANGE_ID: `TAPORTAL-20261015-CRM-PHASE1-011D`; status BLOCKED for end-to-end authorization.
- Corrected action-key lookup to accept UiKey or EndpointKey as existing
  HasFunctionActionAsync does. Read-only manual SQL2014 metadata/mapping handoff
  prepared; actual SQL execution, service-path and concurrency checks NOT_RUN.
- TEAM assignment selection and SQL scope parity require isolated fixtures.
  VIEW unchanged and all three create writers remain Forbidden.

## Phase1-012 development workflow

- CHANGE_ID: `TAPORTAL-20261015-CRM-PHASE1-012`; status PARTIAL.
- Tightened transactional TEAM scope: removed arbitrary MAX(Id) assignment
  selection; require exactly one effective primary/current assignment and
  check its organizational validity. Database integration is still NOT_RUN.
- Create/Change/Revoke and UI are not enabled without transaction-bound target
  eligibility, audit rollback and RowVersion verification. No migrations run.

## Phase1-013 customer employee assignment create

- CHANGE_ID: `TAPORTAL-20261015-CRM-PHASE1-013`; development writer implemented.
- CreateCustomerEmployeeAssignmentAsync performs Customers.Assign authorization,
  target HR/customer-branch checks, overlap detection, relation insert and audit
  inside one Serializable transaction. Branch Share and Service Team writers stay
  Forbidden; no UI, schema change or per-customer grants.
- Offline validation tests passed; SQL integration and concurrency NOT_RUN.

## Phase1-014 branch share and service team create

- CHANGE_ID: `TAPORTAL-20261015-CRM-PHASE1-014`; development writers implemented.
- Both creates use transaction-bound Customers.Share/Customers.Team authorization,
  target organization checks, inclusive overlap protection, relation and audit
  inserts in one Serializable transaction. No grants, schema changes or UI.
- SQL integration including audit rollback and concurrent insert behavior NOT_RUN;
  no production readiness claim. Change/Revoke remain out of scope.

## Phase1-015 relation change and revoke

- CHANGE_ID: `TAPORTAL-20261015-CRM-PHASE1-015`; six development mutation paths added.
- Stored customer identity determines transaction-bound action authorization;
  changes preserve relation target/customer, validate dates, target hierarchy and
  active overlap, and guard updates with RowVersion. Revoke sets IsActive=0;
  repeated revoke conflicts. Both record before/after audit atomically.
- SQL integration, rollback and concurrency remain NOT_RUN; not production-ready.

## Phase1-018 direct development database preflight

- CHANGE_ID: `TAPORTAL-20261015-CRM-PHASE1-018`; status PARTIAL. Owner authorized use of existing TAPortalSys development database; no additional database created.
- Live SQL connection and read-only schema/aggregate checks succeeded. No customers, employees, companies, branches, teams, or employee postings exist; one active user exists. No eligible actor or customer fixture exists for SQL-backed CRM mutations. No SQL writes or cleanup performed.
- Focused CRM tests 38/38; Web.Tests 135/135; Release build passed (zero warnings/errors). These do not prove SQL-backed service behavior. Authenticated UI, all nine writes, scope, concurrency, RowVersion and audit rollback remain NOT_RUN.
- Evidence and remaining requirements: `code-history/20261015-CRM-PHASE1-018-direct-development-database.md`.

## Phase1-019 TAPortalSys fixture and real service workflow

- CHANGE_ID: `TAPORTAL-20261015-CRM-PHASE1-019`; status PARTIAL. Created controlled `TA_DEV_CRM_019` company/HR/users/customers in TAPortalSys without modifying preexisting accounts; no new database, schema migration or automated cleanup.
- Real PortalDb SQL-backed list/detail, deny and three Create → Change → Revoke cycles succeeded; relation and audit readback verified. Repaired actual SQL Server `deny` alias syntax failure by quoting it.
- Full scope/invalid-target/rollback/concurrency matrix and authenticated browser remain pending; fixture identities intentionally have no passwords. Manifest, execution evidence and manual cleanup guidance: `code-history/20261015-CRM-PHASE1-019-fixture-service-verification.md`.

## Phase1-020 authenticated login and security follow-up
- Authenticated HTTP 500 root cause confirmed by Development server exception: Razor component endpoints carried antiforgery metadata without registered antiforgery middleware. `Program.cs` now calls `UseAntiforgery` after authorization; rebuilt application serves authenticated CRM list/detail with HTTP 200 and still redirects anonymous list requests to login. Authenticated Playwright route regression passed. Desktop/mobile relationship workflow tests then reached a genuine fixture overlap validation and did not complete. SQL scope/DENY, audit rollback and concurrency remain unverified; no publish. See `code-history/20261015-CRM-PHASE1-020-auth-security-followup.md`.

- CHANGE_ID: `TAPORTAL-20261015-CRM-PHASE1-020`; status PARTIAL. Added owner-secret opt-in initialization for fixture user 11 using production PasswordService and PortalDb; no password provided, so login and authenticated browser remain NOT_RUN.
- Executed real PortalDb negative authorization checks for ungranted fixture principals and invalid/missing targets. Full scope/deny, audit rollback and concurrency matrix remain NOT_RUN. Evidence and instructions: `code-history/20261015-CRM-PHASE1-020-auth-security-followup.md`.
- Continuation: existing active SYS_ADMIN had no Employee; added a guarded identifiable development HR linkage (no credential changes). SQL-backed PortalDb admin customer list/detail pass. Authenticated login/browser remain NOT_RUN.
- Added `tests/playwright/owner-login.cjs` for local owner-entered normal login and independently validated external storage state; interactive browser is unavailable in the agent execution channel. No authenticated E2E acceptance claimed.