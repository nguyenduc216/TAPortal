# CRM Phase 1 customer scope completion review

- FEATURE_ID: `TAPORTAL-CRM-CUSTOMER-360`
- CHANGE_ID: `TAPORTAL-20261014-CRM-PHASE1-003`
- Status: `BLOCKED`

## Confirmed metadata

`DataScopes` contains active catalog entries ASSIGNED, TEAM, BRANCH, COMPANY,
SELF and CUSTOM. `RoleDataScopes` and `UserDataScopes` contain zero rows. The
catalog therefore does not grant any non-default scope to any role or user.

The implementation must not treat `OwnerBranchId` or `CustomerBranchShares` as
implicit employee visibility. Direct `CustomerEmployeeAssignments` remains the
only employee row grant currently implementable without widening access.

## Blockers

1. There is no confirmed scope assignment to authorize TEAM, BRANCH, COMPANY or
   ALL. The repository also has no confirmed ALL catalog code; `COMPANY` cannot
   be treated as ALL.
2. TEAM cannot be resolved safely: the migration has no `Customer.TeamId`, and
   `CustomerEmployeeAssignments` has no TeamId. A safe TEAM design needs an
   explicit relation, such as a customer-to-service-team relation or a
   time-effective assignment/share relation carrying TeamId, plus a confirmed
   DataScope target contract.
3. The application has no AuditLogs writer and the supplied assignment/share
   tables have no update/revocation contract beyond `IsActive`/`ValidTo`.
   Assignment/share management and same-transaction audit cannot be claimed
   complete without the actual AuditLogs columns and write semantics.

## Actions completed

Added `00c_scope_audit_contract_inspection.sql`, read-only metadata inspection
for DataScopes mappings, assignment/share tables, and AuditLogs. No SQL was
executed, no database was changed, and no Phase 2 work was started.

## Required owner metadata

Run 00c and provide the result sets for column names/types, foreign keys,
constraints, scope assignment counts, and AuditLogs contract. Do not provide
business rows. After that, implement and test the smallest confirmed workflow.