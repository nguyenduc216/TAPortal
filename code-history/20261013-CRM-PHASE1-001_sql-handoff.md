# CRM Phase 1 SQL handoff

- FEATURE_ID: `TAPORTAL-CRM-CUSTOMER-360`
- CHANGE_ID: `TAPORTAL-20261013-CRM-PHASE1-001`
- Status: `SQL_READY_FOR_MANUAL_EXECUTION`

Owner-supplied inspection confirmed the existing Customers, HR, RBAC, DataScopes
and AuditLogs contracts. This change adds read-only RBAC/constraint inspection,
an additive migration for Customers.OwnerBranchId, CustomerEmployeeAssignments and
CustomerBranchShares, and read-only verification. CustomerAccessGrants is not
created: direct employee assignment is represented by CustomerEmployeeAssignments
and role/user scope remains in the existing DataScopes system.

The customer list/export service contract now receives the authenticated user ID
and applies server-side owner/share/employee eligibility filtering, including the
SYS_ADMIN full-view exception. Release build and the full 103-test suite pass.
No SQL was connected to or executed by Codex; no DDL, DML, backup, IIS deployment,
commit, or push was performed. Database-backed acceptance remains unverified.

Next owner action: run `00b_rbac_constraint_inspection.sql`, review its metadata,
then run `01_customer_scope_audit.sql` and `03_verify_customer_scope_audit.sql` in
SSMS in that order. Do not run `02_schema_verification.sql`; it is obsolete.