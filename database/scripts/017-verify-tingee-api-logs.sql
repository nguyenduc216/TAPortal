-- TINGEE-INTEGRATION-CHG-20261104T040031Z-0CF9F3 — READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/014-tingee-api-logs.sql. Expected last line: VERIFY_STATUS=PASS.
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
DECLARE @fail int = 0;
PRINT N'=== TINGEE API LOGS VERIFY ===';
IF OBJECT_ID(N'dbo.TingeeApiLogs', N'U') IS NULL BEGIN PRINT N'FAIL: dbo.TingeeApiLogs missing'; SET @fail += 1; END
ELSE
BEGIN
    PRINT N'PASS: dbo.TingeeApiLogs';
    IF (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.TingeeApiLogs') AND name IN
        (N'Id',N'LogCode',N'Method',N'Url',N'ClientId',N'RequestTimestamp',N'Signature',N'RequestBody',N'HttpStatus',N'ResponseCode',N'ResponseMessage',N'ResponseBody',N'Error',N'DurationMs',N'CreatedAt')) = 15
        PRINT N'PASS: 15 columns';
    ELSE BEGIN PRINT N'FAIL: columns missing'; SET @fail += 1; END
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.TingeeApiLogs') AND name = N'UX_TAL_LogCode' AND is_unique = 1 AND is_disabled = 0)
        PRINT N'PASS: UX_TAL_LogCode';
    ELSE BEGIN PRINT N'FAIL: unique index UX_TAL_LogCode missing/disabled'; SET @fail += 1; END
END
PRINT N'VERIFY_STATUS=' + CASE WHEN @fail = 0 THEN N'PASS' ELSE N'FAIL' END;
