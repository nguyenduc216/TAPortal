-- CUSTOMER-PROFILE — READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/025-audit-logs-admin-columns.sql. Expected last line: VERIFY_STATUS=PASS (NOT_APPLIED before the migration).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
DECLARE @fail int = 0;
DECLARE @cols int = (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.AuditLogs') AND name IN (N'Action', N'EntityName'));
PRINT N'=== AUDIT LOGS ADMIN COLUMNS VERIFY ===';
IF @cols = 0
BEGIN
    PRINT N'INFO: dbo.AuditLogs has no Action/EntityName columns';
    PRINT N'VERIFY_STATUS=NOT_APPLIED';
    RETURN;
END
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
           WHERE c.object_id = OBJECT_ID(N'dbo.AuditLogs') AND c.name = N'Action' AND t.name = N'nvarchar' AND c.max_length >= 200)
    PRINT N'PASS: dbo.AuditLogs.Action nvarchar(100+)'; ELSE BEGIN PRINT N'FAIL: dbo.AuditLogs.Action missing or too short'; SET @fail += 1; END
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
           WHERE c.object_id = OBJECT_ID(N'dbo.AuditLogs') AND c.name = N'EntityName' AND t.name = N'nvarchar' AND c.max_length >= 400)
    PRINT N'PASS: dbo.AuditLogs.EntityName nvarchar(200+)'; ELSE BEGIN PRINT N'FAIL: dbo.AuditLogs.EntityName missing or too short'; SET @fail += 1; END
-- Every column TAPortal.Shared.AuditSql.Insert checks before writing.
IF (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.AuditLogs')
    AND name IN (N'UserId', N'Action', N'EntityName', N'EntityId', N'OldValues', N'NewValues', N'IpAddress')) = 7
    PRINT N'PASS: all 7 columns written by AuditSql.Insert present'; ELSE BEGIN PRINT N'FAIL: a column written by AuditSql.Insert is missing'; SET @fail += 1; END
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.AuditLogs') AND is_nullable = 0
           AND name NOT IN (N'Id', N'CreatedAt') AND is_identity = 0 AND default_object_id = 0)
    BEGIN PRINT N'FAIL: dbo.AuditLogs has a NOT NULL column without default that AuditSql.Insert does not fill'; SET @fail += 1; END
ELSE PRINT N'PASS: no unfilled NOT NULL column';
PRINT N'VERIFY_STATUS=' + CASE WHEN @fail = 0 THEN N'PASS' ELSE N'FAIL' END;
