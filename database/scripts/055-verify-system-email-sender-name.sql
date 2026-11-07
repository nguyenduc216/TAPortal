-- CUSTOMER-PORTAL-FOUNDATION - READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/033-system-email-sender-name.sql. Expected last line: VERIFY_STATUS=PASS (NOT_APPLIED before the migration).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
DECLARE @old nvarchar(200) = N'"fromName":"Ho\u00E1 \u0111\u01A1n t\u1EF1 \u0111\u1ED9ng T.A"';
DECLARE @new nvarchar(200) = N'"fromName":"H\u00F3a \u0111\u01A1n t\u1EF1 \u0111\u1ED9ng"';
DECLARE @oldRows int = (SELECT COUNT(1) FROM dbo.NotificationChannels WHERE Scope = 'SYSTEM' AND Channel = 'SMTP' AND CHARINDEX(@old, Settings) > 0);
DECLARE @newRows int = (SELECT COUNT(1) FROM dbo.NotificationChannels WHERE Scope = 'SYSTEM' AND Channel = 'SMTP' AND CHARINDEX(@new, Settings) > 0);
PRINT N'INFO: SYSTEM SMTP rows with old sender name = ' + CAST(@oldRows AS nvarchar(10)) + N', with new sender name = ' + CAST(@newRows AS nvarchar(10));
IF @oldRows > 0 BEGIN PRINT N'VERIFY_STATUS=NOT_APPLIED'; RETURN; END
IF @newRows = 1 PRINT N'PASS: SYSTEM SMTP sender name is Hoa don tu dong';
PRINT N'VERIFY_STATUS=' + CASE WHEN @newRows = 1 THEN N'PASS' ELSE N'FAIL' END;
