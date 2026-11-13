-- CUSTOMER-PORTAL-FOUNDATION - ROLLBACK of migrations/036-portal-content.sql.
-- Drops dbo.PortalContent and removes the permission rows/grants 036 inserted
-- (CreatedBy = 'A0000036-0000-4000-8000-000000000036'; rows that existed before 036 are never touched).
-- Refuses when edited content exists (staff texts would be lost; export and decide with the operator first), or when a role
-- other than SYS_ADMIN, a direct user permission or a menu uses a permission added by 036.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @Marker uniqueidentifier = 'A0000036-0000-4000-8000-000000000036';
DECLARE @rows int = 0;
IF OBJECT_ID(N'dbo.PortalContent', N'U') IS NOT NULL EXEC sp_executesql N'SELECT @n = COUNT(1) FROM dbo.PortalContent', N'@n int OUTPUT', @n = @rows OUTPUT;
IF @rows > 0
    THROW 51002, 'Rollback refused: edited email templates or terms exist (data would be lost)', 1;
IF EXISTS (SELECT 1 FROM dbo.RolePermissions rp JOIN dbo.Permissions p ON p.Id = rp.PermissionId JOIN dbo.Roles r ON r.Id = rp.RoleId
           WHERE p.CreatedBy = @Marker AND r.Code <> 'SYS_ADMIN')
    THROW 51003, 'Rollback refused: a role other than SYS_ADMIN uses a permission added by migration 036', 1;
IF OBJECT_ID(N'dbo.UserPermissions', N'U') IS NOT NULL
   AND EXISTS (SELECT 1 FROM dbo.UserPermissions up JOIN dbo.Permissions p ON p.Id = up.PermissionId WHERE p.CreatedBy = @Marker)
    THROW 51004, 'Rollback refused: a direct user permission uses a permission added by migration 036', 1;
DECLARE @menuRefs int = 0;
IF OBJECT_ID(N'dbo.MenuPermissions', N'U') IS NOT NULL
    EXEC sp_executesql N'SELECT @n = COUNT(1) FROM dbo.MenuPermissions mp JOIN dbo.Permissions p ON p.Id = mp.PermissionId WHERE p.CreatedBy = @m',
        N'@n int OUTPUT, @m uniqueidentifier', @n = @menuRefs OUTPUT, @m = @Marker;
IF @menuRefs > 0
    THROW 51005, 'Rollback refused: a menu uses a permission added by migration 036', 1;
IF OBJECT_ID(N'dbo.PortalContent', N'U') IS NOT NULL DROP TABLE dbo.PortalContent;
DECLARE @grants int, @permissions int;
DELETE FROM dbo.RolePermissions WHERE CreatedBy = @Marker;
SET @grants = @@ROWCOUNT;
DELETE FROM dbo.Permissions WHERE CreatedBy = @Marker;
SET @permissions = @@ROWCOUNT;
COMMIT;
PRINT N'INFO: SYS_ADMIN grants removed = ' + CAST(@grants AS nvarchar(10));
PRINT N'INFO: permissions removed = ' + CAST(@permissions AS nvarchar(10));
PRINT N'062-rollback-portal-content.sql: OK';
