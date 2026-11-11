-- WEBSITE-TA - READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/035-website-cms.sql. Expected last line: VERIFY_STATUS=PASS (NOT_APPLIED before the migration).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
DECLARE @fail int = 0;
DECLARE @Marker uniqueidentifier = 'A0000035-0000-4000-8000-000000000035';
DECLARE @Tables TABLE (Name sysname NOT NULL PRIMARY KEY);
INSERT @Tables (Name) VALUES (N'WebsitePages'), (N'WebsiteSections'), (N'WebsiteMedia'), (N'WebsitePostCategories'),
    (N'WebsitePosts'), (N'WebsiteSettings'), (N'WebsiteLeads');
DECLARE @Catalogue TABLE (Code varchar(200) COLLATE DATABASE_DEFAULT NOT NULL PRIMARY KEY);
INSERT @Catalogue (Code) VALUES
 ('WEBSITE.VIEW'), ('WEBSITE.CONTENT.EDIT'), ('WEBSITE.POSTS.EDIT'), ('WEBSITE.MEDIA.EDIT'), ('WEBSITE.SETTINGS.EDIT'),
 ('WEBSITE.LEADS.VIEW'), ('WEBSITE.LEADS.EDIT');
DECLARE @tablesTotal int = (SELECT COUNT(1) FROM @Tables);
DECLARE @tablesPresent int = (SELECT COUNT(1) FROM @Tables t WHERE OBJECT_ID(N'dbo.' + t.Name, N'U') IS NOT NULL);
DECLARE @total int = (SELECT COUNT(1) FROM @Catalogue);
DECLARE @present int = (SELECT COUNT(DISTINCT c.Code) FROM @Catalogue c JOIN dbo.Permissions p ON p.Code = c.Code AND p.IsDeleted = 0);
PRINT N'=== WEBSITE CMS VERIFY ===';
IF @tablesPresent = 0 AND @present = 0
BEGIN
    PRINT N'INFO: dbo.Website* tables and WEBSITE.* permissions missing';
    PRINT N'VERIFY_STATUS=NOT_APPLIED';
    RETURN;
END
IF @tablesPresent = @tablesTotal PRINT N'PASS: 7 dbo.Website* tables'; ELSE BEGIN PRINT N'FAIL: dbo.Website* tables missing'; SET @fail += 1; END
IF (SELECT COUNT(*) FROM sys.check_constraints WHERE is_disabled = 0 AND name IN (N'CK_WMd_Type', N'CK_WMd_Size', N'CK_WPC_Slug',
        N'CK_WPo_Status', N'CK_WPo_Published', N'CK_WPo_Slug', N'CK_WLd_Status')) = 7
    PRINT N'PASS: 7 check constraints'; ELSE BEGIN PRINT N'FAIL: a check constraint is missing'; SET @fail += 1; END
IF (SELECT COUNT(*) FROM sys.foreign_keys WHERE name IN (N'FK_WPo_Cover', N'FK_WPo_Category')) = 2
    PRINT N'PASS: 2 foreign keys'; ELSE BEGIN PRINT N'FAIL: a foreign key is missing'; SET @fail += 1; END
IF (SELECT COUNT(*) FROM sys.indexes WHERE name IN (N'IX_WebsiteMedia_Created', N'UX_WebsitePostCategories_Slug', N'UX_WebsitePosts_Slug',
        N'IX_WebsitePosts_Published', N'IX_WebsiteLeads_Status', N'IX_WebsiteLeads_Ip')) = 6
    PRINT N'PASS: 6 indexes'; ELSE BEGIN PRINT N'FAIL: an index is missing'; SET @fail += 1; END
IF @present = @total PRINT N'PASS: every WEBSITE permission exists'; ELSE BEGIN PRINT N'FAIL: permission codes missing'; SET @fail += 1; END
DECLARE @granted int = (SELECT COUNT(DISTINCT c.Code) FROM @Catalogue c JOIN dbo.Permissions p ON p.Code = c.Code AND p.IsDeleted = 0
    JOIN dbo.RolePermissions rp ON rp.PermissionId = p.Id JOIN dbo.Roles r ON r.Id = rp.RoleId AND r.Code = 'SYS_ADMIN');
IF @granted = @total PRINT N'PASS: SYS_ADMIN holds every new permission'; ELSE BEGIN PRINT N'FAIL: SYS_ADMIN lacks new permissions'; SET @fail += 1; END
IF EXISTS (SELECT 1 FROM dbo.Permissions WHERE CreatedBy = @Marker AND Code NOT IN (SELECT Code FROM @Catalogue))
    BEGIN PRINT N'FAIL: marker rows outside the catalogue'; SET @fail += 1; END
ELSE PRINT N'PASS: marker rows match the catalogue';
PRINT N'VERIFY_STATUS=' + CASE WHEN @fail = 0 THEN N'PASS' ELSE N'FAIL' END;
