-- USER-IMPERSONATION - "Truy cập hộ" (operator 2026-11-14): an authorised user signs in under an employee's account after
-- entering a reason; every page opened and every change made in that session is recorded as "<actor> under <account>".
-- MIGRATION_NOT_EXECUTED.
-- Change: USER-IMPERSONATION-CHG-20261114T011414Z-7AE972.
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, after 041.
--   dbo.ImpersonationSessions  one row per "Truy cập hộ": actor, account, reason, start/end, IP, user agent.
--   dbo.ImpersonationActions   every request made inside a session (method, path, HTTP status).
--   dbo.AuditLogs              + ImpersonatorUserId, ImpersonationSessionId (NULL for normal sign-ins): an audited change
--                              keeps UserId = the account used and names the person who really acted.
--   dbo.WorkTaskActivities     + ImpersonatorUserId: the task history shows "<actor> (truy cập hộ <account>)".
--   dbo.Permissions            + AUTH.USERS.IMPERSONATE (granted to SYS_ADMIN; other roles get it on Vai trò & phân quyền).
-- Rows it inserts carry CreatedBy = 'A0000042-0000-4000-8000-000000000042' (marker) so the rollback removes exactly them.
-- New tables and nullable columns only; no existing row is changed. Idempotent.
-- Verify: database/scripts/073-verify-user-impersonation.sql. Rollback: database/scripts/074-rollback-user-impersonation.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL OR OBJECT_ID(N'dbo.AuditLogs', N'U') IS NULL OR OBJECT_ID(N'dbo.WorkTaskActivities', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Permissions', N'U') IS NULL OR OBJECT_ID(N'dbo.RolePermissions', N'U') IS NULL OR OBJECT_ID(N'dbo.Roles', N'U') IS NULL
 THROW 51001, 'Precondition failed: dbo.Users, dbo.AuditLogs, dbo.WorkTaskActivities, dbo.Permissions, dbo.RolePermissions or dbo.Roles missing', 1;
DECLARE @SysAdmin uniqueidentifier = (SELECT TOP 1 Id FROM dbo.Roles WHERE Code = 'SYS_ADMIN' AND IsDeleted = 0);
IF @SysAdmin IS NULL
 THROW 51001, 'Precondition failed: role SYS_ADMIN missing', 1;
DECLARE @Marker uniqueidentifier = 'A0000042-0000-4000-8000-000000000042';

IF OBJECT_ID(N'dbo.ImpersonationSessions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ImpersonationSessions (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_ImpersonationSessions PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        ActorUserId uniqueidentifier NOT NULL,
        TargetUserId uniqueidentifier NOT NULL,
        Reason nvarchar(500) NOT NULL,
        StartedAt datetime2(3) NOT NULL CONSTRAINT DF_ImpersonationSessions_StartedAt DEFAULT SYSUTCDATETIME(),
        ExpiresAt datetime2(3) NOT NULL,
        EndedAt datetime2(3) NULL,
        EndReason varchar(20) NULL,
        IpAddress varchar(64) NULL,
        UserAgent nvarchar(300) NULL,
        CONSTRAINT FK_ImpersonationSessions_Actor FOREIGN KEY (ActorUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT FK_ImpersonationSessions_Target FOREIGN KEY (TargetUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT CK_ImpersonationSessions_NotSelf CHECK (ActorUserId <> TargetUserId),
        CONSTRAINT CK_ImpersonationSessions_EndReason CHECK (EndReason IS NULL OR EndReason IN ('EXIT','LOGOUT','TIMEOUT','REVOKED'))
    );
    CREATE INDEX IX_ImpersonationSessions_StartedAt ON dbo.ImpersonationSessions(StartedAt DESC) INCLUDE (ActorUserId, TargetUserId, EndedAt);
    CREATE INDEX IX_ImpersonationSessions_Target ON dbo.ImpersonationSessions(TargetUserId, StartedAt DESC);
    PRINT N'dbo.ImpersonationSessions: created';
END

IF OBJECT_ID(N'dbo.ImpersonationActions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ImpersonationActions (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ImpersonationActions PRIMARY KEY,
        SessionId uniqueidentifier NOT NULL,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_ImpersonationActions_CreatedAt DEFAULT SYSUTCDATETIME(),
        Method varchar(10) NOT NULL,
        Path nvarchar(400) NOT NULL,
        StatusCode int NOT NULL,
        CONSTRAINT FK_ImpersonationActions_Session FOREIGN KEY (SessionId) REFERENCES dbo.ImpersonationSessions(Id)
    );
    CREATE INDEX IX_ImpersonationActions_Session ON dbo.ImpersonationActions(SessionId, CreatedAt);
    PRINT N'dbo.ImpersonationActions: created';
END

IF COL_LENGTH(N'dbo.AuditLogs', N'ImpersonatorUserId') IS NULL
BEGIN
    ALTER TABLE dbo.AuditLogs ADD ImpersonatorUserId uniqueidentifier NULL, ImpersonationSessionId uniqueidentifier NULL;
    PRINT N'dbo.AuditLogs: ImpersonatorUserId, ImpersonationSessionId added';
END

IF COL_LENGTH(N'dbo.WorkTaskActivities', N'ImpersonatorUserId') IS NULL
BEGIN
    ALTER TABLE dbo.WorkTaskActivities ADD ImpersonatorUserId uniqueidentifier NULL;
    PRINT N'dbo.WorkTaskActivities: ImpersonatorUserId added';
END

IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Code = 'AUTH.USERS.IMPERSONATE' AND IsDeleted = 0)
BEGIN
    INSERT dbo.Permissions (Id, Code, Name, ModuleCode, FunctionCode, ActionCode, Description, IsSystem, IsActive, CreatedBy)
    VALUES (NEWID(), 'AUTH.USERS.IMPERSONATE', N'Sign in on behalf of a user (impersonation)', 'SYSTEM', 'USERS', 'IMPERSONATE', N'TAPortal Admin (migration 042)', 1, 1, @Marker);
    PRINT N'dbo.Permissions: AUTH.USERS.IMPERSONATE inserted';
END
INSERT dbo.RolePermissions (RoleId, PermissionId, CreatedBy)
SELECT @SysAdmin, p.Id, @Marker FROM dbo.Permissions p
WHERE p.Code = 'AUTH.USERS.IMPERSONATE' AND p.IsDeleted = 0
  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermissions rp WHERE rp.RoleId = @SysAdmin AND rp.PermissionId = p.Id);
PRINT N'INFO: SYS_ADMIN grants inserted = ' + CAST(@@ROWCOUNT AS nvarchar(10));
COMMIT TRANSACTION;
PRINT N'042-user-impersonation.sql: OK';
