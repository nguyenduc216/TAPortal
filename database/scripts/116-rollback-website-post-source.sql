-- WEBSITE-TA - rollback of migrations/064-website-post-source.sql: drops CK_WebsitePosts_SourceKind and the WebsitePosts
-- Source* columns (article types and sources entered after 064 are lost). After it, 115-verify-website-post-source.sql must
-- print VERIFY_STATUS=NOT_APPLIED.
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.CK_WebsitePosts_SourceKind', N'C') IS NOT NULL BEGIN EXEC (N'ALTER TABLE dbo.WebsitePosts DROP CONSTRAINT CK_WebsitePosts_SourceKind;'); PRINT N'CK_WebsitePosts_SourceKind: dropped'; END
IF COL_LENGTH(N'dbo.WebsitePosts', N'SourceKind') IS NOT NULL BEGIN EXEC (N'ALTER TABLE dbo.WebsitePosts DROP COLUMN SourceKind;'); PRINT N'SourceKind: dropped'; END
IF COL_LENGTH(N'dbo.WebsitePosts', N'SourceName') IS NOT NULL BEGIN EXEC (N'ALTER TABLE dbo.WebsitePosts DROP COLUMN SourceName;'); PRINT N'SourceName: dropped'; END
IF COL_LENGTH(N'dbo.WebsitePosts', N'SourceUrl') IS NOT NULL BEGIN EXEC (N'ALTER TABLE dbo.WebsitePosts DROP COLUMN SourceUrl;'); PRINT N'SourceUrl: dropped'; END
IF COL_LENGTH(N'dbo.WebsitePosts', N'SourceAuthor') IS NOT NULL BEGIN EXEC (N'ALTER TABLE dbo.WebsitePosts DROP COLUMN SourceAuthor;'); PRINT N'SourceAuthor: dropped'; END
IF COL_LENGTH(N'dbo.WebsitePosts', N'SourcePublishedOn') IS NOT NULL BEGIN EXEC (N'ALTER TABLE dbo.WebsitePosts DROP COLUMN SourcePublishedOn;'); PRINT N'SourcePublishedOn: dropped'; END
COMMIT TRANSACTION;
PRINT N'116-rollback-website-post-source: done';
