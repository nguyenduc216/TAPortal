-- CONTRACTS-AND-TASKS - READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/065-service-periods.sql. Expected last line: VERIFY_STATUS=PASS (NOT_APPLIED before the migration).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
IF OBJECT_ID(N'dbo.ServiceTasks', N'U') IS NULL AND OBJECT_ID(N'dbo.ServicePeriods', N'U') IS NULL AND OBJECT_ID(N'dbo.WorkTaskQuantities', N'U') IS NULL
   AND COL_LENGTH(N'dbo.WorkTasks', N'ServicePeriodId') IS NULL
BEGIN PRINT N'INFO: service periods missing'; PRINT N'VERIFY_STATUS=NOT_APPLIED'; RETURN; END
DECLARE @ok bit = 1;
IF OBJECT_ID(N'dbo.ServiceTasks', N'U') IS NOT NULL PRINT N'PASS: dbo.ServiceTasks'; ELSE BEGIN PRINT N'FAIL: dbo.ServiceTasks'; SET @ok = 0; END
IF OBJECT_ID(N'dbo.ServicePeriods', N'U') IS NOT NULL PRINT N'PASS: dbo.ServicePeriods'; ELSE BEGIN PRINT N'FAIL: dbo.ServicePeriods'; SET @ok = 0; END
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ServicePeriods_CustomerServiceMonth' AND is_unique = 1) PRINT N'PASS: one period per customer, service and month'; ELSE BEGIN PRINT N'FAIL: UX_ServicePeriods_CustomerServiceMonth'; SET @ok = 0; END
IF OBJECT_ID(N'dbo.WorkTaskQuantities', N'U') IS NOT NULL PRINT N'PASS: dbo.WorkTaskQuantities'; ELSE BEGIN PRINT N'FAIL: dbo.WorkTaskQuantities'; SET @ok = 0; END
IF COL_LENGTH(N'dbo.WorkTasks', N'ServicePeriodId') IS NOT NULL AND COL_LENGTH(N'dbo.WorkTasks', N'Unit') IS NOT NULL AND COL_LENGTH(N'dbo.WorkTasks', N'MetricCode') IS NOT NULL AND COL_LENGTH(N'dbo.WorkTasks', N'ServiceSortOrder') IS NOT NULL
    PRINT N'PASS: WorkTasks columns'; ELSE BEGIN PRINT N'FAIL: WorkTasks columns'; SET @ok = 0; END
IF OBJECT_ID(N'dbo.FK_WorkTasks_ServicePeriod', N'F') IS NOT NULL PRINT N'PASS: FK_WorkTasks_ServicePeriod'; ELSE BEGIN PRINT N'FAIL: FK_WorkTasks_ServicePeriod'; SET @ok = 0; END
PRINT N'VERIFY_STATUS=' + CASE WHEN @ok = 1 THEN N'PASS' ELSE N'FAIL' END;
