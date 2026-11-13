-- CONTRACTS-AND-TASKS - rollback of migrations/041-customer-staff.sql.
-- Drops only dbo.CustomerStaff (created by 041). Assignments entered after 041 are lost with it.
-- After it, database/scripts/071-verify-customer-staff.sql must print VERIFY_STATUS=NOT_APPLIED.
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.CustomerStaff', N'U') IS NOT NULL BEGIN DROP TABLE dbo.CustomerStaff; PRINT N'dbo.CustomerStaff: dropped'; END
COMMIT TRANSACTION;
PRINT N'072-rollback-customer-staff: done';
