-- CUSTOMER-PROFILE — ROLLBACK of migrations/025-audit-logs-admin-columns.sql.
-- Drops dbo.AuditLogs.Action and EntityName. Refuses once the applications have written audit rows into them (the audit
-- trail must not be lost): decide with the operator first.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @used int = 0;
IF COL_LENGTH(N'dbo.AuditLogs', N'Action') IS NOT NULL
    EXEC sys.sp_executesql N'SELECT @n = COUNT(*) FROM dbo.AuditLogs WHERE Action IS NOT NULL', N'@n int OUTPUT', @n = @used OUTPUT;
IF @used > 0
    THROW 51002, 'Rollback refused: dbo.AuditLogs.Action holds audit rows written by the applications', 1;
IF COL_LENGTH(N'dbo.AuditLogs', N'Action') IS NOT NULL ALTER TABLE dbo.AuditLogs DROP COLUMN Action;
IF COL_LENGTH(N'dbo.AuditLogs', N'EntityName') IS NOT NULL ALTER TABLE dbo.AuditLogs DROP COLUMN EntityName;
COMMIT;
PRINT N'040-rollback-audit-logs-admin-columns.sql: OK';
