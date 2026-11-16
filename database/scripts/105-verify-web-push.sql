-- CONTRACTS-AND-TASKS - READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/059-web-push.sql. Expected last line: VERIFY_STATUS=PASS (NOT_APPLIED before the migration).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
IF OBJECT_ID(N'dbo.WebPushKeys', N'U') IS NULL AND OBJECT_ID(N'dbo.WebPushSubscriptions', N'U') IS NULL BEGIN PRINT N'INFO: web push tables missing'; PRINT N'VERIFY_STATUS=NOT_APPLIED'; RETURN; END
DECLARE @ok bit = 1;
IF OBJECT_ID(N'dbo.WebPushKeys', N'U') IS NOT NULL PRINT N'PASS: dbo.WebPushKeys'; ELSE BEGIN PRINT N'FAIL: dbo.WebPushKeys'; SET @ok = 0; END
IF OBJECT_ID(N'dbo.CK_WebPushKeys_Single', N'C') IS NOT NULL PRINT N'PASS: single key row'; ELSE BEGIN PRINT N'FAIL: CK_WebPushKeys_Single'; SET @ok = 0; END
IF OBJECT_ID(N'dbo.WebPushSubscriptions', N'U') IS NOT NULL PRINT N'PASS: dbo.WebPushSubscriptions'; ELSE BEGIN PRINT N'FAIL: dbo.WebPushSubscriptions'; SET @ok = 0; END
IF OBJECT_ID(N'dbo.FK_WebPushSubscriptions_User', N'F') IS NOT NULL PRINT N'PASS: FK to Users'; ELSE BEGIN PRINT N'FAIL: FK_WebPushSubscriptions_User'; SET @ok = 0; END
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_WebPushSubscriptions_Endpoint' AND is_unique = 1 AND object_id = OBJECT_ID(N'dbo.WebPushSubscriptions')) PRINT N'PASS: UX_WebPushSubscriptions_Endpoint'; ELSE BEGIN PRINT N'FAIL: UX_WebPushSubscriptions_Endpoint'; SET @ok = 0; END
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WebPushSubscriptions_User' AND object_id = OBJECT_ID(N'dbo.WebPushSubscriptions')) PRINT N'PASS: IX_WebPushSubscriptions_User'; ELSE BEGIN PRINT N'FAIL: IX_WebPushSubscriptions_User'; SET @ok = 0; END
PRINT N'VERIFY_STATUS=' + CASE WHEN @ok = 1 THEN N'PASS' ELSE N'FAIL' END;
