-- USER-IMPERSONATION - rollback of migrations/042-user-impersonation.sql.
-- Drops dbo.ImpersonationActions, dbo.ImpersonationSessions, the impersonation columns of dbo.AuditLogs and
-- dbo.WorkTaskActivities, and the AUTH.USERS.IMPERSONATE permission inserted by 042 (marker row) with its grants.
-- The impersonation history recorded after 042 is lost with it.
-- After it, database/scripts/073-verify-user-impersonation.sql must print VERIFY_STATUS=NOT_APPLIED.
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @Marker uniqueidentifier = 'A0000042-0000-4000-8000-000000000042';
IF OBJECT_ID(N'dbo.ImpersonationActions', N'U') IS NOT NULL BEGIN DROP TABLE dbo.ImpersonationActions; PRINT N'dbo.ImpersonationActions: dropped'; END
IF OBJECT_ID(N'dbo.ImpersonationSessions', N'U') IS NOT NULL BEGIN DROP TABLE dbo.ImpersonationSessions; PRINT N'dbo.ImpersonationSessions: dropped'; END
IF COL_LENGTH(N'dbo.AuditLogs', N'ImpersonatorUserId') IS NOT NULL
BEGIN
    EXEC(N'ALTER TABLE dbo.AuditLogs DROP COLUMN ImpersonatorUserId, ImpersonationSessionId;');
    PRINT N'dbo.AuditLogs: impersonation columns dropped';
END
IF COL_LENGTH(N'dbo.WorkTaskActivities', N'ImpersonatorUserId') IS NOT NULL
BEGIN
    EXEC(N'ALTER TABLE dbo.WorkTaskActivities DROP COLUMN ImpersonatorUserId;');
    PRINT N'dbo.WorkTaskActivities: ImpersonatorUserId dropped';
END
DECLARE @perm TABLE (Id uniqueidentifier PRIMARY KEY);
INSERT @perm SELECT Id FROM dbo.Permissions WHERE Code = 'AUTH.USERS.IMPERSONATE' AND CreatedBy = @Marker;
DELETE rp FROM dbo.RolePermissions rp JOIN @perm p ON p.Id = rp.PermissionId;
PRINT N'INFO: role grants removed = ' + CAST(@@ROWCOUNT AS nvarchar(10));
IF OBJECT_ID(N'dbo.UserPermissions', N'U') IS NOT NULL
BEGIN
    DELETE up FROM dbo.UserPermissions up JOIN @perm p ON p.Id = up.PermissionId;
    PRINT N'INFO: user grants removed = ' + CAST(@@ROWCOUNT AS nvarchar(10));
END
DELETE pm FROM dbo.Permissions pm JOIN @perm p ON p.Id = pm.Id;
PRINT N'INFO: permissions removed = ' + CAST(@@ROWCOUNT AS nvarchar(10));
COMMIT TRANSACTION;
PRINT N'074-rollback-user-impersonation: done';
