-- ADMIN-ROLE-PERMISSIONS — ROLLBACK of migrations/028-admin-permission-catalogue.sql.
-- Removes only the rows that migration 028 inserted, identified by CreatedBy = 'A0000028-0000-4000-8000-000000000028'
-- (a fixed marker the application never writes: it stores the acting user's id):
--   1. dbo.RolePermissions grants of SYS_ADMIN added by 028 (grants that existed before 028 keep their own CreatedBy);
--   2. dbo.Permissions rows added by 028 (codes that existed before 028 are never touched).
-- Refuses while a role other than SYS_ADMIN, a direct user permission or a menu uses a permission added by 028: remove
-- those grants in the Admin portal first (Vai trò & phân quyền), or decide with the operator.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @Marker uniqueidentifier = 'A0000028-0000-4000-8000-000000000028';
IF EXISTS (SELECT 1 FROM dbo.RolePermissions rp JOIN dbo.Permissions p ON p.Id = rp.PermissionId JOIN dbo.Roles r ON r.Id = rp.RoleId
           WHERE p.CreatedBy = @Marker AND r.Code <> 'SYS_ADMIN')
    THROW 51002, 'Rollback refused: a role other than SYS_ADMIN uses a permission added by migration 028', 1;
IF OBJECT_ID(N'dbo.UserPermissions', N'U') IS NOT NULL
   AND EXISTS (SELECT 1 FROM dbo.UserPermissions up JOIN dbo.Permissions p ON p.Id = up.PermissionId WHERE p.CreatedBy = @Marker)
    THROW 51003, 'Rollback refused: a direct user permission uses a permission added by migration 028', 1;
DECLARE @menuRefs int = 0;
IF OBJECT_ID(N'dbo.MenuPermissions', N'U') IS NOT NULL
    EXEC sp_executesql N'SELECT @n = COUNT(1) FROM dbo.MenuPermissions mp JOIN dbo.Permissions p ON p.Id = mp.PermissionId WHERE p.CreatedBy = @m',
        N'@n int OUTPUT, @m uniqueidentifier', @n = @menuRefs OUTPUT, @m = @Marker;
IF @menuRefs > 0
    THROW 51004, 'Rollback refused: a menu uses a permission added by migration 028', 1;
DECLARE @grants int, @permissions int;
DELETE FROM dbo.RolePermissions WHERE CreatedBy = @Marker;
SET @grants = @@ROWCOUNT;
DELETE FROM dbo.Permissions WHERE CreatedBy = @Marker;
SET @permissions = @@ROWCOUNT;
COMMIT;
PRINT N'INFO: SYS_ADMIN grants removed = ' + CAST(@grants AS nvarchar(10));
PRINT N'INFO: catalogue permissions removed = ' + CAST(@permissions AS nvarchar(10));
PRINT N'046-rollback-admin-permission-catalogue.sql: OK';
