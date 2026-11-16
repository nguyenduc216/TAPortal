-- CONTRACTS-AND-TASKS - READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/060-work-push-notifications.sql. Expected last line: VERIFY_STATUS=PASS (NOT_APPLIED before the migration).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
DECLARE @table bit = CASE WHEN OBJECT_ID(N'dbo.WebPushState', N'U') IS NULL THEN 0 ELSE 1 END;
DECLARE @index bit = CASE WHEN EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WorkTaskActivities_CreatedAt' AND object_id = OBJECT_ID(N'dbo.WorkTaskActivities')) THEN 1 ELSE 0 END;
IF @table = 0 AND @index = 0 BEGIN PRINT N'INFO: dbo.WebPushState and IX_WorkTaskActivities_CreatedAt missing'; PRINT N'VERIFY_STATUS=NOT_APPLIED'; RETURN; END
DECLARE @ok bit = 1;
IF @table = 1 PRINT N'PASS: dbo.WebPushState'; ELSE BEGIN PRINT N'FAIL: dbo.WebPushState'; SET @ok = 0; END
IF OBJECT_ID(N'dbo.CK_WebPushState_Single', N'C') IS NOT NULL PRINT N'PASS: single state row'; ELSE BEGIN PRINT N'FAIL: CK_WebPushState_Single'; SET @ok = 0; END
IF @index = 1 PRINT N'PASS: IX_WorkTaskActivities_CreatedAt'; ELSE BEGIN PRINT N'FAIL: IX_WorkTaskActivities_CreatedAt'; SET @ok = 0; END
PRINT N'VERIFY_STATUS=' + CASE WHEN @ok = 1 THEN N'PASS' ELSE N'FAIL' END;
