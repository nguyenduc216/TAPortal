-- TINGEE-INTEGRATION-CHG-20261104T040031Z-0CF9F3 — ROLLBACK of migrations/014-tingee-api-logs.sql.
-- Drops dbo.TingeeApiLogs ONLY when it is empty; once calls were logged it aborts without changes (operator decides).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.TingeeApiLogs', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.TingeeApiLogs)
 THROW 51002, 'Rollback refused: dbo.TingeeApiLogs contains data; nothing dropped', 1;
IF OBJECT_ID(N'dbo.TingeeApiLogs', N'U') IS NOT NULL DROP TABLE dbo.TingeeApiLogs;
COMMIT;
PRINT N'018-rollback-tingee-api-logs.sql: OK (table dropped)';
