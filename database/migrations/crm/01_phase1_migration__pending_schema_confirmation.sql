/*
 FEATURE_ID: TAPORTAL-CRM-CUSTOMER-360
 CHANGE_ID: TAPORTAL-20261013-CRM-PHASE1-001
 ORDER: 01 (DO NOT RUN yet)
 STATUS: BLOCKED_PENDING_SCHEMA_INSPECTION

 No CRM DDL is intentionally present in this file. The Phase 1 contract requires
 existing Customers/organization/employee/audit schemas to be reconciled first.
 Running guessed DDL could change production semantics or break existing data.
 After the owner supplies the output of 00_schema_inspection.sql, this file must
 be replaced by a reviewed additive migration with explicit columns/FKs/indexes.
*/
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys' THROW 52310,'Wrong database; expected TAPortalSys.',1;
THROW 52311,'CRM Phase 1 migration is intentionally blocked pending schema inspection results.',1;