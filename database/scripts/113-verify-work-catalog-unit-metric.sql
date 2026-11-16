-- CONTRACTS-AND-TASKS - READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/063-work-catalog-unit-metric.sql. Expected last line: VERIFY_STATUS=PASS (NOT_APPLIED before the migration).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
IF COL_LENGTH(N'dbo.WorkTaskCatalog', N'Unit') IS NULL AND COL_LENGTH(N'dbo.WorkTaskCatalog', N'MetricCode') IS NULL BEGIN PRINT N'INFO: columns missing'; PRINT N'VERIFY_STATUS=NOT_APPLIED'; RETURN; END
DECLARE @ok bit = 1;
IF COL_LENGTH(N'dbo.WorkTaskCatalog', N'Unit') = 100 PRINT N'PASS: Unit nvarchar(50)'; ELSE BEGIN PRINT N'FAIL: Unit'; SET @ok = 0; END
IF COL_LENGTH(N'dbo.WorkTaskCatalog', N'MetricCode') = 20 PRINT N'PASS: MetricCode varchar(20)'; ELSE BEGIN PRINT N'FAIL: MetricCode'; SET @ok = 0; END
PRINT N'VERIFY_STATUS=' + CASE WHEN @ok = 1 THEN N'PASS' ELSE N'FAIL' END;
