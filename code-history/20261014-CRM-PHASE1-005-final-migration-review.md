# CRM Phase 1 final migration review

- FEATURE_ID: `TAPORTAL-CRM-CUSTOMER-360`
- CHANGE_ID: `TAPORTAL-20261014-CRM-PHASE1-005`
- Status: `READY_FOR_MANUAL_SQL`

## Audit contract

All preflight and verification checks now use the actual `dbo.AuditLogs`
contract: `UserId`, `ModuleCode`, `FunctionCode`, `ActionCode`, `EntityType`,
`EntityId`, `OldValues`, `NewValues`, and `CreatedAt` (plus the existing
request metadata columns). No `Action` or `EntityName` column is added or
expected. Later CRM writers must use `ActionCode` and `EntityType`.

## Official SQL set

Run `00_schema_inspection.sql`, `00b_rbac_constraint_inspection.sql`,
`00c_scope_audit_contract_inspection.sql`, `01_customer_scope_audit.sql`, and
`03_verify_customer_scope_audit.sql`. `01_customer_scope_audit.sql` is the only
official DDL migration. The pending migration and `02_schema_verification.sql`
are obsolete and must not be run.

## Migration properties

SQL Server 12.x / compatibility 120 exact guards are retained. The DDL is additive,
transactional, uses XACT_ABORT and TRY/CATCH, and contains no DROP or DELETE.
Existing objects are not recreated. Relation tables retain effective/history
columns and receive RowVersion only when no SQL rowversion/timestamp exists.

## Validation

SQL was not executed. Database-backed verification remains `NOT_RUN`.