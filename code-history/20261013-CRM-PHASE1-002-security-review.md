# CRM Phase 1 security review

- FEATURE_ID: `TAPORTAL-CRM-CUSTOMER-360`
- CHANGE_ID: `TAPORTAL-20261013-CRM-PHASE1-002`
- Status: `BLOCKED`

## Review result

The customer list previously treated `OwnerBranchId` and active
`CustomerBranchShares` as implicit visibility. That violated the 1A–18A
decision: employee access defaults to direct customer assignment and
organization scopes require explicit RBAC/DataScopes authorization. The
implicit grants were removed. `GetCustomersAsync(Guid userId)` now requires the
current CRM view action before querying and only implements direct,
currently-effective `CustomerEmployeeAssignments` for employee row visibility.

Explicit user DENY is checked before the SYS_ADMIN shortcut in function
authorization. Account validity and the existing HR employment gate remain
mandatory. SYS_ADMIN is not made subject to an employee assignment, but it is
still subject to active account, valid role membership, function mapping, and
explicit DENY.

The repository does not expose a sufficiently confirmed DataScopes assignment
contract for Team/Branch/ALL target resolution. Those scope expansions remain
fail-closed rather than relying on guessed columns or tables. Assignment/share
management workflows and AuditLogs writes are not claimed complete: the SQL
schema is additive, but no application write service for these operations exists
in the current source.

## SQL status

`00b_rbac_constraint_inspection.sql` remains read-only and now checks SQL Server
2014+ and compatibility level 120+. `01_customer_scope_audit.sql` remains
transactional/idempotent and now checks key types before DDL. Neither script was
executed. Database-backed validation is required from the owner.

## Validation

- Release build: PASS, 0 warnings, 0 errors.
- Unit tests: PASS, 106 passed, 0 failed, 0 skipped.
- Integration tests: NOT_RUN.
- SQL execution/deployment: NOT_RUN.