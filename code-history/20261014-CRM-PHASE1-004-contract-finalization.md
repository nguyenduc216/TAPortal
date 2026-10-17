# CRM Phase 1 contract finalization

- FEATURE_ID: `TAPORTAL-CRM-CUSTOMER-360`
- CHANGE_ID: `TAPORTAL-20261014-CRM-PHASE1-004`
- Status: `SQL_READY_FOR_MANUAL_EXECUTION`

## Contract

ASSIGNED uses `CustomerEmployeeAssignments.EmployeeId`; TEAM uses the explicit
`CustomerServiceTeams(CustomerId,TeamId)` relation; BRANCH uses
`Customers.OwnerBranchId` and `CustomerBranchShares.BranchId`; COMPANY resolves
through `Branches.CompanyId`. SELF and CUSTOM are deliberately not used in CRM
Phase 1. No DataScopes or scope mappings are seeded.

## Schema changes

The transactional additive migration now adds/guards `CustomerServiceTeams` and
adds `rowversion` concurrency tokens to all three scope relation tables. It
retains effective dates, historical rows, foreign keys, indexes and date checks.
Existing objects are not recreated. A pre-existing incomplete
`CustomerServiceTeams` contract aborts before commit rather than being silently
altered.

## Execution

Run `00_schema_inspection.sql`, `00b_rbac_constraint_inspection.sql`,
`00c_scope_audit_contract_inspection.sql`, `01_customer_scope_audit.sql`, then
`03_verify_customer_scope_audit.sql` manually in SSMS. Do not run the obsolete
`02_schema_verification.sql`. Codex did not execute SQL or modify the database.

Application assignment/share workflow, same-transaction AuditLogs writes and
database-backed integration acceptance remain intentionally deferred until the
owner confirms migration PASS, as required by the implementation stop point.