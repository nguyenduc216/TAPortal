-- WEBSITE-TA - Website media: images, videos and PDF files (operator 2026-11-17: "đổi thành media, gồm thư viện ảnh, video,
-- file PDF"; video "Cả hai" = YouTube link or MP4 upload; PDF "Tải về từ bài viết").
--   dbo.WebsiteMedia + ExternalUrl nvarchar(500) NULL (YouTube link; row ContentType 'video/youtube', no bytes).
--   CK_WMd_Type: + 'video/mp4', 'video/youtube', 'application/pdf'.
--   CK_WMd_Size: images 1 B - 5 MB, PDF up to 10 MB, MP4 up to 25 MB; a YouTube row has 0 bytes and an ExternalUrl.
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, after 065.
-- Adds a nullable column and widens two CHECK constraints; no existing row is changed (every stored image still passes). Idempotent.
-- Verify: database/scripts/119-verify-website-media-kinds.sql. Rollback: database/scripts/120-rollback-website-media-kinds.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.WebsiteMedia', N'U') IS NULL
 THROW 51001, 'Precondition failed: dbo.WebsiteMedia missing (migration 035)', 1;
IF COL_LENGTH(N'dbo.WebsiteMedia', N'ExternalUrl') IS NULL
BEGIN
    ALTER TABLE dbo.WebsiteMedia ADD ExternalUrl nvarchar(500) NULL;
    PRINT N'dbo.WebsiteMedia.ExternalUrl: added';
END
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_WMd_Type' AND parent_object_id = OBJECT_ID(N'dbo.WebsiteMedia')
               AND definition LIKE N'%application/pdf%')
BEGIN
    IF OBJECT_ID(N'dbo.CK_WMd_Type', N'C') IS NOT NULL EXEC (N'ALTER TABLE dbo.WebsiteMedia DROP CONSTRAINT CK_WMd_Type;');
    EXEC (N'ALTER TABLE dbo.WebsiteMedia WITH CHECK ADD CONSTRAINT CK_WMd_Type CHECK (ContentType IN
        (''image/png'',''image/jpeg'',''image/webp'',''image/gif'',''video/mp4'',''video/youtube'',''application/pdf''));');
    PRINT N'CK_WMd_Type: images, video/mp4, video/youtube, application/pdf';
END
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_WMd_Size' AND parent_object_id = OBJECT_ID(N'dbo.WebsiteMedia')
               AND definition LIKE N'%ExternalUrl%')
BEGIN
    IF OBJECT_ID(N'dbo.CK_WMd_Size', N'C') IS NOT NULL EXEC (N'ALTER TABLE dbo.WebsiteMedia DROP CONSTRAINT CK_WMd_Size;');
    EXEC (N'ALTER TABLE dbo.WebsiteMedia WITH CHECK ADD CONSTRAINT CK_WMd_Size CHECK (
        (ContentType = ''video/youtube'' AND ByteSize = 0 AND DATALENGTH(Data) = 0 AND ExternalUrl IS NOT NULL)
        OR (ContentType <> ''video/youtube'' AND ByteSize = DATALENGTH(Data) AND ByteSize >= 1 AND ByteSize <=
            CASE ContentType WHEN ''video/mp4'' THEN 26214400 WHEN ''application/pdf'' THEN 10485760 ELSE 5242880 END));');
    PRINT N'CK_WMd_Size: image 5 MB, PDF 10 MB, MP4 25 MB, YouTube link without bytes';
END
COMMIT TRANSACTION;
PRINT N'066-website-media-kinds: done';
