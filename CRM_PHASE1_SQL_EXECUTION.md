# CRM Phase 1 SQL execution handoff

- FEATURE_ID: `TAPORTAL-CRM-CUSTOMER-360`
- CHANGE_ID: `TAPORTAL-20261014-CRM-PHASE1-005`
- Database: `TAPortalSys`
- Current handoff status: **SQL_READY_FOR_MANUAL_EXECUTION**

## Important limitation

The contract is now additive and explicit: ASSIGNED references
`CustomerEmployeeAssignments.EmployeeId`, TEAM references
`CustomerServiceTeams.TeamId`, BRANCH references owner/share branch, and COMPANY
resolves through `Branches.CompanyId`. SELF and CUSTOM are not used in CRM Phase
1. DataScope mappings remain owned by the existing RBAC system; this migration
does not seed or recreate DataScopes, RoleDataScopes or UserDataScopes.

Audit events reuse `dbo.AuditLogs`: `ModuleCode=CRM`, `FunctionCode` is the
secured CRM function, `ActionCode` is `ASSIGN`, `CHANGE` or `REVOKE`,
`EntityType` is `CustomerAssignment`, `CustomerBranchShare` or
`CustomerServiceTeam`, `EntityId` is the relation identifier, and
`OldValues`/`NewValues` contain serialized before/after values. No `Action` or
`EntityName` columns are expected or created.

## Files and order

1. `database/migrations/crm/00_schema_inspection.sql` — baseline inspection.
2. `database/migrations/crm/00b_rbac_constraint_inspection.sql` — SQL Server/compatibility/RBAC metadata.
3. `database/migrations/crm/00c_scope_audit_contract_inspection.sql` — scope/audit/table contract metadata.
4. `database/migrations/crm/01_customer_scope_audit.sql` — transactional additive migration.
5. `database/migrations/crm/03_verify_customer_scope_audit.sql` — read-only verification.
6. `database/migrations/crm/02_schema_verification.sql` — obsolete template; do not use.

The old `01_phase1_migration__pending_schema_confirmation.sql` is also obsolete
and must not be run. `01_customer_scope_audit.sql` is the only official DDL
migration in this set.

## Preconditions

Use SSMS against the intended SQL Server instance, select database `TAPortalSys`,
confirm the query window database selector, and save the complete Results/Grid
and Messages output. No backup or database creation is requested from Codex.

## Expected result

Scripts 00/00b/00c return database/version/compatibility metadata, presence and columns
for Customers, HR, authorization and candidate scope/audit objects, FK/index
metadata, and ends with `NO CRM DDL WAS EXECUTED`. It must not print customer,
employee, username, password, or other business values.

## How to return results

Send the `.sql` script output or exported CSV/text for every result set plus SSMS
Messages. Redact connection strings, secrets, hashes, personal data and row data.
The owner should execute script 01 only after reviewing 00b and 00c; script 02
remains obsolete.

## Rollback

Inspection has no changes and therefore needs no rollback. Any later reviewed
DDL migration must document its own transaction/rollback. If a later change has
already committed and cannot be safely reversed with additive compensating DDL,
the owner must use the project-approved restore procedure; Codex does not run it.