-- CONTRACTS-AND-TASKS - READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/056-work-catalog-checklist.sql. Expected last line: VERIFY_STATUS=PASS (NOT_APPLIED before the migration).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
IF COL_LENGTH(N'dbo.WorkTaskCatalog', N'ChecklistText') IS NULL BEGIN PRINT N'INFO: WorkTaskCatalog.ChecklistText missing'; PRINT N'VERIFY_STATUS=NOT_APPLIED'; RETURN; END
IF COL_LENGTH(N'dbo.WorkTaskCatalog', N'ChecklistText') = 8000 PRINT N'PASS: WorkTaskCatalog.ChecklistText nvarchar(4000)';
PRINT N'VERIFY_STATUS=' + CASE WHEN COL_LENGTH(N'dbo.WorkTaskCatalog', N'ChecklistText') = 8000 THEN N'PASS' ELSE N'FAIL' END;
