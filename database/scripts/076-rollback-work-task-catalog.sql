-- CONTRACTS-AND-TASKS - rollback of migrations/043-work-task-catalog.sql.
-- Drops dbo.WorkTaskCatalog (entries entered after 043 are lost with it; task names already saved are kept) and the
-- TASKS.CATALOG.MANAGE permission inserted by 043 (marker row) with its grants.
-- After it, database/scripts/075-verify-work-task-catalog.sql must print VERIFY_STATUS=NOT_APPLIED.
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @Marker uniqueidentifier = 'A0000043-0000-4000-8000-000000000043';
IF OBJECT_ID(N'dbo.WorkTaskCatalog', N'U') IS NOT NULL BEGIN DROP TABLE dbo.WorkTaskCatalog; PRINT N'dbo.WorkTaskCatalog: dropped'; END
DECLARE @perm TABLE (Id uniqueidentifier PRIMARY KEY);
INSERT @perm SELECT Id FROM dbo.Permissions WHERE Code = 'TASKS.CATALOG.MANAGE' AND CreatedBy = @Marker;
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
PRINT N'076-rollback-work-task-catalog: done';
