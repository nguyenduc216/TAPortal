-- CONTRACTS-AND-TASKS - READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/057-work-catalog-checklist-guides.sql. Expected last line: VERIFY_STATUS=PASS (NOT_APPLIED before the migration).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
IF COL_LENGTH(N'dbo.WorkTaskCatalog', N'ChecklistJson') IS NULL BEGIN PRINT N'INFO: WorkTaskCatalog.ChecklistJson missing'; PRINT N'VERIFY_STATUS=NOT_APPLIED'; RETURN; END
IF COL_LENGTH(N'dbo.WorkTaskCatalog', N'ChecklistJson') = -1 PRINT N'PASS: WorkTaskCatalog.ChecklistJson nvarchar(max)';
PRINT N'VERIFY_STATUS=' + CASE WHEN COL_LENGTH(N'dbo.WorkTaskCatalog', N'ChecklistJson') = -1 THEN N'PASS' ELSE N'FAIL' END;
