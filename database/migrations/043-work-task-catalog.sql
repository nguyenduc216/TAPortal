-- CONTRACTS-AND-TASKS - Danh mục công việc (operator 2026-11-14): a task's name is chosen from a managed list of
-- task types ("Kéo hóa đơn", "Nhập sao kê ngân hàng", ...); typing suggests matching entries and free text is refused,
-- so the same work always has the same name. Details go to the task description.
-- MIGRATION_NOT_EXECUTED.
-- Change: CONTRACTS-AND-TASKS-CHG-20261114T021052Z-F6F9C9.
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, after 042.
--   dbo.WorkTaskCatalog   one row per task name (unique among non-deleted rows), optional description, order, active flag.
--   dbo.Permissions       + TASKS.CATALOG.MANAGE (granted to SYS_ADMIN; other roles get it on Vai trò & phân quyền).
-- The catalogue entries themselves are business data entered on Công việc > Danh mục công việc (not seeded here).
-- Rows it inserts carry CreatedBy = 'A0000043-0000-4000-8000-000000000043' (marker) so the rollback removes exactly them.
-- New table only; no existing row is changed. Idempotent.
-- Verify: database/scripts/075-verify-work-task-catalog.sql. Rollback: database/scripts/076-rollback-work-task-catalog.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.WorkTasks', N'U') IS NULL OR OBJECT_ID(N'dbo.Permissions', N'U') IS NULL OR OBJECT_ID(N'dbo.RolePermissions', N'U') IS NULL OR OBJECT_ID(N'dbo.Roles', N'U') IS NULL
 THROW 51001, 'Precondition failed: dbo.WorkTasks, dbo.Permissions, dbo.RolePermissions or dbo.Roles missing', 1;
DECLARE @SysAdmin uniqueidentifier = (SELECT TOP 1 Id FROM dbo.Roles WHERE Code = 'SYS_ADMIN' AND IsDeleted = 0);
IF @SysAdmin IS NULL
 THROW 51001, 'Precondition failed: role SYS_ADMIN missing', 1;
DECLARE @Marker uniqueidentifier = 'A0000043-0000-4000-8000-000000000043';

IF OBJECT_ID(N'dbo.WorkTaskCatalog', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WorkTaskCatalog (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_WorkTaskCatalog PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        Name nvarchar(200) NOT NULL,
        Description nvarchar(500) NULL,
        SortOrder int NOT NULL CONSTRAINT DF_WorkTaskCatalog_SortOrder DEFAULT 0,
        IsActive bit NOT NULL CONSTRAINT DF_WorkTaskCatalog_IsActive DEFAULT 1,
        IsDeleted bit NOT NULL CONSTRAINT DF_WorkTaskCatalog_IsDeleted DEFAULT 0,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_WorkTaskCatalog_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        UpdatedAt datetime2(3) NULL,
        UpdatedBy uniqueidentifier NULL,
        CONSTRAINT CK_WorkTaskCatalog_Name CHECK (LEN(Name) > 0)
    );
    CREATE UNIQUE INDEX UX_WorkTaskCatalog_Name ON dbo.WorkTaskCatalog(Name) WHERE IsDeleted = 0;
    PRINT N'dbo.WorkTaskCatalog: created';
END

IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Code = 'TASKS.CATALOG.MANAGE' AND IsDeleted = 0)
BEGIN
    INSERT dbo.Permissions (Id, Code, Name, ModuleCode, FunctionCode, ActionCode, Description, IsSystem, IsActive, CreatedBy)
    VALUES (NEWID(), 'TASKS.CATALOG.MANAGE', N'Manage the task name catalogue', 'TASKS', 'CATALOG', 'MANAGE', N'TAPortal Admin (migration 043)', 1, 1, @Marker);
    PRINT N'dbo.Permissions: TASKS.CATALOG.MANAGE inserted';
END
INSERT dbo.RolePermissions (RoleId, PermissionId, CreatedBy)
SELECT @SysAdmin, p.Id, @Marker FROM dbo.Permissions p
WHERE p.Code = 'TASKS.CATALOG.MANAGE' AND p.IsDeleted = 0
  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermissions rp WHERE rp.RoleId = @SysAdmin AND rp.PermissionId = p.Id);
PRINT N'INFO: SYS_ADMIN grants inserted = ' + CAST(@@ROWCOUNT AS nvarchar(10));
COMMIT TRANSACTION;
PRINT N'043-work-task-catalog.sql: OK';
