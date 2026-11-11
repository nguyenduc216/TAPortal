-- WEBSITE-TA - content of the public website tuvangiaiphapta.vn, edited in the Admin portal "Website TA" section.
-- MIGRATION_NOT_EXECUTED.
-- Change: WEBSITE-TA-CHG-20261111T163408Z-D94863.
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, after 034.
--   dbo.WebsitePages          SEO title/description and publish flag of a page declared in code (TAWebsite.Core SiteMap).
--   dbo.WebsiteSections       edited content (JSON) of one section of a page, its order and visibility. Pages and sections are
--                             declared in code with default content; a row exists only once a section has been edited.
--   dbo.WebsiteMedia          images used by the website (PNG/JPEG/WEBP/GIF up to 5 MB, kept in the database).
--   dbo.WebsitePostCategories categories of news articles.
--   dbo.WebsitePosts          news articles: draft / published (scheduled when PublishedAt is in the future).
--   dbo.WebsiteSettings       key/value settings shown on every page (hotline, email, address, map, social links...).
--   dbo.WebsiteLeads          contact and quote requests sent from the website form, and how T.A handled them.
-- Permissions WEBSITE.* (module "Website TA") granted to SYS_ADMIN; rows carry CreatedBy = marker A0000035-....
-- New tables and inserted permission rows only; no existing row is changed. Idempotent.
-- Verify: database/scripts/059-verify-website-cms.sql. Rollback: database/scripts/060-rollback-website-cms.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.Permissions', N'U') IS NULL OR OBJECT_ID(N'dbo.RolePermissions', N'U') IS NULL OR OBJECT_ID(N'dbo.Roles', N'U') IS NULL
 THROW 51001, 'Precondition failed: dbo.Permissions, dbo.RolePermissions or dbo.Roles missing', 1;
DECLARE @SysAdmin uniqueidentifier = (SELECT TOP 1 Id FROM dbo.Roles WHERE Code = 'SYS_ADMIN' AND IsDeleted = 0);
IF @SysAdmin IS NULL
 THROW 51001, 'Precondition failed: role SYS_ADMIN missing', 1;
DECLARE @Marker uniqueidentifier = 'A0000035-0000-4000-8000-000000000035';

IF OBJECT_ID(N'dbo.WebsitePages', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WebsitePages (
        PageKey varchar(60) NOT NULL CONSTRAINT PK_WebsitePages PRIMARY KEY,
        SeoTitle nvarchar(200) NULL,
        SeoDescription nvarchar(400) NULL,
        IsPublished bit NOT NULL CONSTRAINT DF_WPg_IsPublished DEFAULT 1,
        UpdatedAt datetime2(3) NOT NULL CONSTRAINT DF_WPg_UpdatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedBy uniqueidentifier NULL,
        RowVersion rowversion NOT NULL
    );
    PRINT N'dbo.WebsitePages: created';
END ELSE PRINT N'dbo.WebsitePages: exists';

IF OBJECT_ID(N'dbo.WebsiteSections', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WebsiteSections (
        PageKey varchar(60) NOT NULL,
        SectionKey varchar(60) NOT NULL,
        ContentJson nvarchar(max) NULL,
        SortOrder int NOT NULL,
        IsVisible bit NOT NULL CONSTRAINT DF_WSec_IsVisible DEFAULT 1,
        UpdatedAt datetime2(3) NOT NULL CONSTRAINT DF_WSec_UpdatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedBy uniqueidentifier NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT PK_WebsiteSections PRIMARY KEY (PageKey, SectionKey)
    );
    PRINT N'dbo.WebsiteSections: created';
END ELSE PRINT N'dbo.WebsiteSections: exists';

IF OBJECT_ID(N'dbo.WebsiteMedia', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WebsiteMedia (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_WebsiteMedia PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        FileName nvarchar(260) NOT NULL,
        ContentType varchar(20) NOT NULL,
        ByteSize int NOT NULL,
        Width int NULL,
        Height int NULL,
        AltText nvarchar(300) NULL,
        Data varbinary(max) NOT NULL,
        Sha256 char(64) NOT NULL,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_WMd_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        IsDeleted bit NOT NULL CONSTRAINT DF_WMd_IsDeleted DEFAULT 0,
        CONSTRAINT CK_WMd_Type CHECK (ContentType IN ('image/png','image/jpeg','image/webp','image/gif')),
        CONSTRAINT CK_WMd_Size CHECK (ByteSize = DATALENGTH(Data) AND ByteSize BETWEEN 1 AND 5242880)
    );
    EXEC(N'CREATE INDEX IX_WebsiteMedia_Created ON dbo.WebsiteMedia(IsDeleted, CreatedAt);');
    PRINT N'dbo.WebsiteMedia: created';
END ELSE PRINT N'dbo.WebsiteMedia: exists';

IF OBJECT_ID(N'dbo.WebsitePostCategories', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WebsitePostCategories (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_WebsitePostCategories PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        Slug varchar(120) NOT NULL,
        Name nvarchar(150) NOT NULL,
        SortOrder int NOT NULL CONSTRAINT DF_WPC_SortOrder DEFAULT 0,
        IsDeleted bit NOT NULL CONSTRAINT DF_WPC_IsDeleted DEFAULT 0,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_WPC_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt datetime2(3) NOT NULL CONSTRAINT DF_WPC_UpdatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedBy uniqueidentifier NULL,
        CONSTRAINT CK_WPC_Slug CHECK (Slug NOT LIKE '%[^a-z0-9-]%' AND LEN(Slug) > 0)
    );
    EXEC(N'CREATE UNIQUE INDEX UX_WebsitePostCategories_Slug ON dbo.WebsitePostCategories(Slug) WHERE IsDeleted = 0;');
    PRINT N'dbo.WebsitePostCategories: created';
END ELSE PRINT N'dbo.WebsitePostCategories: exists';

IF OBJECT_ID(N'dbo.WebsitePosts', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WebsitePosts (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_WebsitePosts PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        Slug varchar(200) NOT NULL,
        Title nvarchar(300) NOT NULL,
        Summary nvarchar(1000) NULL,
        BodyHtml nvarchar(max) NULL,
        CoverMediaId uniqueidentifier NULL,
        CategoryId uniqueidentifier NULL,
        AuthorName nvarchar(150) NULL,
        Tags nvarchar(500) NULL,
        Status tinyint NOT NULL CONSTRAINT DF_WPo_Status DEFAULT 0,
        PublishedAt datetime2(3) NULL,
        IsFeatured bit NOT NULL CONSTRAINT DF_WPo_IsFeatured DEFAULT 0,
        SeoTitle nvarchar(200) NULL,
        SeoDescription nvarchar(400) NULL,
        ViewCount int NOT NULL CONSTRAINT DF_WPo_ViewCount DEFAULT 0,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_WPo_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        UpdatedAt datetime2(3) NOT NULL CONSTRAINT DF_WPo_UpdatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedBy uniqueidentifier NULL,
        IsDeleted bit NOT NULL CONSTRAINT DF_WPo_IsDeleted DEFAULT 0,
        RowVersion rowversion NOT NULL,
        CONSTRAINT FK_WPo_Cover FOREIGN KEY (CoverMediaId) REFERENCES dbo.WebsiteMedia(Id),
        CONSTRAINT FK_WPo_Category FOREIGN KEY (CategoryId) REFERENCES dbo.WebsitePostCategories(Id),
        CONSTRAINT CK_WPo_Status CHECK (Status IN (0, 1)),
        CONSTRAINT CK_WPo_Published CHECK (Status = 0 OR PublishedAt IS NOT NULL),
        CONSTRAINT CK_WPo_Slug CHECK (Slug NOT LIKE '%[^a-z0-9-]%' AND LEN(Slug) > 0)
    );
    EXEC(N'CREATE UNIQUE INDEX UX_WebsitePosts_Slug ON dbo.WebsitePosts(Slug) WHERE IsDeleted = 0;');
    EXEC(N'CREATE INDEX IX_WebsitePosts_Published ON dbo.WebsitePosts(Status, IsDeleted, PublishedAt) INCLUDE (CategoryId, IsFeatured);');
    PRINT N'dbo.WebsitePosts: created';
END ELSE PRINT N'dbo.WebsitePosts: exists';

IF OBJECT_ID(N'dbo.WebsiteSettings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WebsiteSettings (
        SettingKey varchar(80) NOT NULL CONSTRAINT PK_WebsiteSettings PRIMARY KEY,
        Value nvarchar(max) NULL,
        UpdatedAt datetime2(3) NOT NULL CONSTRAINT DF_WSet_UpdatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedBy uniqueidentifier NULL
    );
    PRINT N'dbo.WebsiteSettings: created';
END ELSE PRINT N'dbo.WebsiteSettings: exists';

IF OBJECT_ID(N'dbo.WebsiteLeads', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WebsiteLeads (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_WebsiteLeads PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_WLd_CreatedAt DEFAULT SYSUTCDATETIME(),
        FullName nvarchar(150) NOT NULL,
        Phone varchar(30) NOT NULL,
        Email nvarchar(200) NULL,
        CompanyName nvarchar(250) NULL,
        Services nvarchar(500) NULL,
        CompanyType nvarchar(150) NULL,
        Scale nvarchar(200) NULL,
        ContactTime nvarchar(100) NULL,
        Message nvarchar(2000) NULL,
        SourcePage nvarchar(300) NULL,
        ClientIpHash char(64) NULL,
        Status tinyint NOT NULL CONSTRAINT DF_WLd_Status DEFAULT 0,
        Note nvarchar(2000) NULL,
        UpdatedAt datetime2(3) NULL,
        UpdatedBy uniqueidentifier NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT CK_WLd_Status CHECK (Status BETWEEN 0 AND 4)
    );
    EXEC(N'CREATE INDEX IX_WebsiteLeads_Status ON dbo.WebsiteLeads(Status, CreatedAt);');
    EXEC(N'CREATE INDEX IX_WebsiteLeads_Ip ON dbo.WebsiteLeads(ClientIpHash, CreatedAt);');
    PRINT N'dbo.WebsiteLeads: created';
END ELSE PRINT N'dbo.WebsiteLeads: exists';

DECLARE @Catalogue TABLE (Code varchar(200) COLLATE DATABASE_DEFAULT NOT NULL PRIMARY KEY, Name nvarchar(250) NOT NULL, ModuleCode varchar(100) NOT NULL, FunctionCode varchar(100) NOT NULL, ActionCode varchar(50) NOT NULL);
INSERT @Catalogue (Code, Name, ModuleCode, FunctionCode, ActionCode) VALUES
 ('WEBSITE.VIEW',           N'Open the Website TA section and preview content',      'WEBSITE', 'WEBSITE',  'VIEW'),
 ('WEBSITE.CONTENT.EDIT',   N'Edit website pages, sections and SEO',                 'WEBSITE', 'CONTENT',  'EDIT'),
 ('WEBSITE.POSTS.EDIT',     N'Write, publish and delete news articles',              'WEBSITE', 'POSTS',    'EDIT'),
 ('WEBSITE.MEDIA.EDIT',     N'Upload and delete website images',                     'WEBSITE', 'MEDIA',    'EDIT'),
 ('WEBSITE.SETTINGS.EDIT',  N'Edit website contact details and general settings',    'WEBSITE', 'SETTINGS', 'EDIT'),
 ('WEBSITE.LEADS.VIEW',     N'View contact and quote requests from the website',     'WEBSITE', 'LEADS',    'VIEW'),
 ('WEBSITE.LEADS.EDIT',     N'Update the status and notes of website requests',      'WEBSITE', 'LEADS',    'EDIT');
INSERT dbo.Permissions (Id, Code, Name, ModuleCode, FunctionCode, ActionCode, Description, IsSystem, IsActive, CreatedBy)
SELECT NEWID(), c.Code, c.Name, c.ModuleCode, c.FunctionCode, c.ActionCode, N'TAPortal Admin (migration 035)', 1, 1, @Marker
FROM @Catalogue c
WHERE NOT EXISTS (SELECT 1 FROM dbo.Permissions p WHERE p.Code = c.Code AND p.IsDeleted = 0);
INSERT dbo.RolePermissions (RoleId, PermissionId, CreatedBy)
SELECT @SysAdmin, p.Id, @Marker
FROM @Catalogue c JOIN dbo.Permissions p ON p.Code = c.Code AND p.IsDeleted = 0
WHERE NOT EXISTS (SELECT 1 FROM dbo.RolePermissions rp WHERE rp.RoleId = @SysAdmin AND rp.PermissionId = p.Id);
COMMIT;
PRINT N'035-website-cms.sql: OK';
