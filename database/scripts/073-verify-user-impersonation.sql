-- USER-IMPERSONATION - READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/042-user-impersonation.sql. Expected last line: VERIFY_STATUS=PASS (NOT_APPLIED before the migration).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
IF OBJECT_ID(N'dbo.ImpersonationSessions', N'U') IS NULL BEGIN PRINT N'INFO: dbo.ImpersonationSessions missing'; PRINT N'VERIFY_STATUS=NOT_APPLIED'; RETURN; END
DECLARE @fail int = 0;
IF OBJECT_ID(N'dbo.ImpersonationActions', N'U') IS NULL BEGIN PRINT N'FAIL: dbo.ImpersonationActions missing'; SET @fail += 1; END
IF COL_LENGTH(N'dbo.ImpersonationSessions', N'Reason') IS NULL OR COL_LENGTH(N'dbo.ImpersonationSessions', N'ExpiresAt') IS NULL
    BEGIN PRINT N'FAIL: ImpersonationSessions columns missing'; SET @fail += 1; END
IF OBJECT_ID(N'dbo.CK_ImpersonationSessions_NotSelf', N'C') IS NULL BEGIN PRINT N'FAIL: CK_ImpersonationSessions_NotSelf missing'; SET @fail += 1; END
IF COL_LENGTH(N'dbo.AuditLogs', N'ImpersonatorUserId') IS NULL OR COL_LENGTH(N'dbo.AuditLogs', N'ImpersonationSessionId') IS NULL
    BEGIN PRINT N'FAIL: AuditLogs impersonation columns missing'; SET @fail += 1; END
IF COL_LENGTH(N'dbo.WorkTaskActivities', N'ImpersonatorUserId') IS NULL BEGIN PRINT N'FAIL: WorkTaskActivities.ImpersonatorUserId missing'; SET @fail += 1; END
IF NOT EXISTS (SELECT 1 FROM dbo.Permissions p JOIN dbo.RolePermissions rp ON rp.PermissionId = p.Id JOIN dbo.Roles r ON r.Id = rp.RoleId
               WHERE p.Code = 'AUTH.USERS.IMPERSONATE' AND p.IsDeleted = 0 AND r.Code = 'SYS_ADMIN')
    BEGIN PRINT N'FAIL: AUTH.USERS.IMPERSONATE not granted to SYS_ADMIN'; SET @fail += 1; END
IF @fail = 0 PRINT N'PASS: impersonation sessions, actions, audit columns and permission';
PRINT N'VERIFY_STATUS=' + CASE WHEN @fail = 0 THEN N'PASS' ELSE N'FAIL' END;
