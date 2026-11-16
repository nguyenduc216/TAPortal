-- TINGEE-INTEGRATION (notifications) - READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/055-notification-portal-scope.sql. Expected last line: VERIFY_STATUS=PASS (NOT_APPLIED before the migration).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
DECLARE @nc nvarchar(max) = (SELECT definition FROM sys.check_constraints WHERE name = N'CK_NC_Scope' AND parent_object_id = OBJECT_ID(N'dbo.NotificationChannels'));
DECLARE @no nvarchar(max) = (SELECT definition FROM sys.check_constraints WHERE name = N'CK_NO_Scope' AND parent_object_id = OBJECT_ID(N'dbo.NotificationOutbox'));
DECLARE @ncOk bit = CASE WHEN CHARINDEX(N'PORTAL', ISNULL(@nc, N'')) > 0 THEN 1 ELSE 0 END;
DECLARE @noOk bit = CASE WHEN CHARINDEX(N'PORTAL', ISNULL(@no, N'')) > 0 THEN 1 ELSE 0 END;
PRINT N'INFO: CK_NC_Scope accepts PORTAL = ' + CAST(@ncOk AS nvarchar(1)) + N', CK_NO_Scope accepts PORTAL = ' + CAST(@noOk AS nvarchar(1));
IF @nc IS NULL OR @no IS NULL BEGIN PRINT N'FAIL: scope constraint missing'; PRINT N'VERIFY_STATUS=FAIL'; RETURN; END
IF @ncOk = 0 AND @noOk = 0 BEGIN PRINT N'VERIFY_STATUS=NOT_APPLIED'; RETURN; END
PRINT N'VERIFY_STATUS=' + CASE WHEN @ncOk = 1 AND @noOk = 1 THEN N'PASS' ELSE N'FAIL' END;
