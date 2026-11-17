-- WEBSITE-TA - rollback of migrations/066-website-media-kinds.sql: restores the image-only CK_WMd_Type / CK_WMd_Size of
-- migration 035 and drops WebsiteMedia.ExternalUrl. It stops (no change) while videos or PDF files are stored: soft-deleted
-- rows included, they must be removed by the operator first. After it, 119-verify-website-media-kinds.sql must print
-- VERIFY_STATUS=NOT_APPLIED.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF EXISTS (SELECT 1 FROM dbo.WebsiteMedia WHERE ContentType NOT IN ('image/png','image/jpeg','image/webp','image/gif'))
 THROW 51002, 'Rollback stopped: dbo.WebsiteMedia holds videos or PDF files; no changes executed', 1;
IF OBJECT_ID(N'dbo.CK_WMd_Size', N'C') IS NOT NULL EXEC (N'ALTER TABLE dbo.WebsiteMedia DROP CONSTRAINT CK_WMd_Size;');
IF OBJECT_ID(N'dbo.CK_WMd_Type', N'C') IS NOT NULL EXEC (N'ALTER TABLE dbo.WebsiteMedia DROP CONSTRAINT CK_WMd_Type;');
EXEC (N'ALTER TABLE dbo.WebsiteMedia WITH CHECK ADD CONSTRAINT CK_WMd_Type CHECK (ContentType IN (''image/png'',''image/jpeg'',''image/webp'',''image/gif''));');
EXEC (N'ALTER TABLE dbo.WebsiteMedia WITH CHECK ADD CONSTRAINT CK_WMd_Size CHECK (ByteSize = DATALENGTH(Data) AND ByteSize BETWEEN 1 AND 5242880);');
PRINT N'CK_WMd_Type, CK_WMd_Size: restored (images only, 5 MB)';
IF COL_LENGTH(N'dbo.WebsiteMedia', N'ExternalUrl') IS NOT NULL BEGIN EXEC (N'ALTER TABLE dbo.WebsiteMedia DROP COLUMN ExternalUrl;'); PRINT N'ExternalUrl: dropped'; END
COMMIT TRANSACTION;
PRINT N'120-rollback-website-media-kinds: done';
