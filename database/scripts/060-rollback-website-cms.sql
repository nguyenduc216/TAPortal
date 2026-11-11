-- WEBSITE-TA - ROLLBACK of migrations/035-website-cms.sql.
-- Drops the 7 dbo.Website* tables and removes the permission rows/grants 035 inserted
-- (CreatedBy = 'A0000035-0000-4000-8000-000000000035'; rows that existed before 035 are never touched).
-- Refuses when website content exists (edited sections, images, articles, settings or requests: data would be lost; export
-- and decide with the operator first), or when a role other than SYS_ADMIN, a direct user permission or a menu uses a
-- permission added by 035.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @Marker uniqueidentifier = 'A0000035-0000-4000-8000-000000000035';
DECLARE @rows int = 0, @n int;
IF OBJECT_ID(N'dbo.WebsiteSections', N'U') IS NOT NULL BEGIN EXEC sp_executesql N'SELECT @n = COUNT(1) FROM dbo.WebsiteSections', N'@n int OUTPUT', @n = @n OUTPUT; SET @rows += @n; END
IF OBJECT_ID(N'dbo.WebsiteMedia', N'U') IS NOT NULL BEGIN EXEC sp_executesql N'SELECT @n = COUNT(1) FROM dbo.WebsiteMedia', N'@n int OUTPUT', @n = @n OUTPUT; SET @rows += @n; END
IF OBJECT_ID(N'dbo.WebsitePosts', N'U') IS NOT NULL BEGIN EXEC sp_executesql N'SELECT @n = COUNT(1) FROM dbo.WebsitePosts', N'@n int OUTPUT', @n = @n OUTPUT; SET @rows += @n; END
IF OBJECT_ID(N'dbo.WebsiteSettings', N'U') IS NOT NULL BEGIN EXEC sp_executesql N'SELECT @n = COUNT(1) FROM dbo.WebsiteSettings', N'@n int OUTPUT', @n = @n OUTPUT; SET @rows += @n; END
IF OBJECT_ID(N'dbo.WebsiteLeads', N'U') IS NOT NULL BEGIN EXEC sp_executesql N'SELECT @n = COUNT(1) FROM dbo.WebsiteLeads', N'@n int OUTPUT', @n = @n OUTPUT; SET @rows += @n; END
IF @rows > 0
    THROW 51002, 'Rollback refused: website content or requests exist (data would be lost)', 1;
IF EXISTS (SELECT 1 FROM dbo.RolePermissions rp JOIN dbo.Permissions p ON p.Id = rp.PermissionId JOIN dbo.Roles r ON r.Id = rp.RoleId
           WHERE p.CreatedBy = @Marker AND r.Code <> 'SYS_ADMIN')
    THROW 51003, 'Rollback refused: a role other than SYS_ADMIN uses a permission added by migration 035', 1;
IF OBJECT_ID(N'dbo.UserPermissions', N'U') IS NOT NULL
   AND EXISTS (SELECT 1 FROM dbo.UserPermissions up JOIN dbo.Permissions p ON p.Id = up.PermissionId WHERE p.CreatedBy = @Marker)
    THROW 51004, 'Rollback refused: a direct user permission uses a permission added by migration 035', 1;
DECLARE @menuRefs int = 0;
IF OBJECT_ID(N'dbo.MenuPermissions', N'U') IS NOT NULL
    EXEC sp_executesql N'SELECT @n = COUNT(1) FROM dbo.MenuPermissions mp JOIN dbo.Permissions p ON p.Id = mp.PermissionId WHERE p.CreatedBy = @m',
        N'@n int OUTPUT, @m uniqueidentifier', @n = @menuRefs OUTPUT, @m = @Marker;
IF @menuRefs > 0
    THROW 51005, 'Rollback refused: a menu uses a permission added by migration 035', 1;
IF OBJECT_ID(N'dbo.WebsiteLeads', N'U') IS NOT NULL DROP TABLE dbo.WebsiteLeads;
IF OBJECT_ID(N'dbo.WebsiteSettings', N'U') IS NOT NULL DROP TABLE dbo.WebsiteSettings;
IF OBJECT_ID(N'dbo.WebsitePosts', N'U') IS NOT NULL DROP TABLE dbo.WebsitePosts;
IF OBJECT_ID(N'dbo.WebsitePostCategories', N'U') IS NOT NULL DROP TABLE dbo.WebsitePostCategories;
IF OBJECT_ID(N'dbo.WebsiteMedia', N'U') IS NOT NULL DROP TABLE dbo.WebsiteMedia;
IF OBJECT_ID(N'dbo.WebsiteSections', N'U') IS NOT NULL DROP TABLE dbo.WebsiteSections;
IF OBJECT_ID(N'dbo.WebsitePages', N'U') IS NOT NULL DROP TABLE dbo.WebsitePages;
DECLARE @grants int, @permissions int;
DELETE FROM dbo.RolePermissions WHERE CreatedBy = @Marker;
SET @grants = @@ROWCOUNT;
DELETE FROM dbo.Permissions WHERE CreatedBy = @Marker;
SET @permissions = @@ROWCOUNT;
COMMIT;
PRINT N'INFO: SYS_ADMIN grants removed = ' + CAST(@grants AS nvarchar(10));
PRINT N'INFO: permissions removed = ' + CAST(@permissions AS nvarchar(10));
PRINT N'060-rollback-website-cms.sql: OK';
