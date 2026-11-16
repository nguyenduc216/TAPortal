-- WEBSITE-TA - Website posts: article type and source (operator 2026-11-17: "Làm như đề xuất" – loại bài "Tóm tắt có trích nguồn").
-- Each article records whether T.A wrote it (0), summarised another site with quotes (1) or republished it with written
-- permission (2), plus the original site, link, author and date shown in the "Nguồn" box on the website.
--   dbo.WebsitePosts + SourceKind tinyint NULL (NULL = 0), SourceName nvarchar(200) NULL, SourceUrl nvarchar(500) NULL,
--                      SourceAuthor nvarchar(200) NULL, SourcePublishedOn date NULL; CHECK SourceKind IN (0,1,2).
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, after 063.
-- Nullable columns only; no existing row is changed. Idempotent.
-- Verify: database/scripts/115-verify-website-post-source.sql. Rollback: database/scripts/116-rollback-website-post-source.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.WebsitePosts', N'U') IS NULL
 THROW 51001, 'Precondition failed: dbo.WebsitePosts missing (migration 035)', 1;
IF COL_LENGTH(N'dbo.WebsitePosts', N'SourceKind') IS NULL
BEGIN
    ALTER TABLE dbo.WebsitePosts ADD SourceKind tinyint NULL;
    PRINT N'dbo.WebsitePosts.SourceKind: added';
END
IF COL_LENGTH(N'dbo.WebsitePosts', N'SourceName') IS NULL
BEGIN
    ALTER TABLE dbo.WebsitePosts ADD SourceName nvarchar(200) NULL;
    PRINT N'dbo.WebsitePosts.SourceName: added';
END
IF COL_LENGTH(N'dbo.WebsitePosts', N'SourceUrl') IS NULL
BEGIN
    ALTER TABLE dbo.WebsitePosts ADD SourceUrl nvarchar(500) NULL;
    PRINT N'dbo.WebsitePosts.SourceUrl: added';
END
IF COL_LENGTH(N'dbo.WebsitePosts', N'SourceAuthor') IS NULL
BEGIN
    ALTER TABLE dbo.WebsitePosts ADD SourceAuthor nvarchar(200) NULL;
    PRINT N'dbo.WebsitePosts.SourceAuthor: added';
END
IF COL_LENGTH(N'dbo.WebsitePosts', N'SourcePublishedOn') IS NULL
BEGIN
    ALTER TABLE dbo.WebsitePosts ADD SourcePublishedOn date NULL;
    PRINT N'dbo.WebsitePosts.SourcePublishedOn: added';
END
IF OBJECT_ID(N'dbo.CK_WebsitePosts_SourceKind', N'C') IS NULL
BEGIN
    EXEC (N'ALTER TABLE dbo.WebsitePosts WITH CHECK ADD CONSTRAINT CK_WebsitePosts_SourceKind CHECK (SourceKind IS NULL OR SourceKind IN (0, 1, 2));');
    PRINT N'CK_WebsitePosts_SourceKind: added';
END
COMMIT TRANSACTION;
PRINT N'064-website-post-source: done';
