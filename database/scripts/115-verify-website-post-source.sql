-- WEBSITE-TA - READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/064-website-post-source.sql. Expected last line: VERIFY_STATUS=PASS (NOT_APPLIED before the migration).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
IF COL_LENGTH(N'dbo.WebsitePosts', N'SourceKind') IS NULL AND COL_LENGTH(N'dbo.WebsitePosts', N'SourceName') IS NULL
   AND COL_LENGTH(N'dbo.WebsitePosts', N'SourceUrl') IS NULL AND COL_LENGTH(N'dbo.WebsitePosts', N'SourceAuthor') IS NULL
   AND COL_LENGTH(N'dbo.WebsitePosts', N'SourcePublishedOn') IS NULL AND OBJECT_ID(N'dbo.CK_WebsitePosts_SourceKind', N'C') IS NULL
BEGIN PRINT N'INFO: source columns missing'; PRINT N'VERIFY_STATUS=NOT_APPLIED'; RETURN; END
DECLARE @ok bit = 1;
IF COL_LENGTH(N'dbo.WebsitePosts', N'SourceKind') = 1 PRINT N'PASS: SourceKind tinyint'; ELSE BEGIN PRINT N'FAIL: SourceKind'; SET @ok = 0; END
IF COL_LENGTH(N'dbo.WebsitePosts', N'SourceName') = 400 PRINT N'PASS: SourceName nvarchar(200)'; ELSE BEGIN PRINT N'FAIL: SourceName'; SET @ok = 0; END
IF COL_LENGTH(N'dbo.WebsitePosts', N'SourceUrl') = 1000 PRINT N'PASS: SourceUrl nvarchar(500)'; ELSE BEGIN PRINT N'FAIL: SourceUrl'; SET @ok = 0; END
IF COL_LENGTH(N'dbo.WebsitePosts', N'SourceAuthor') = 400 PRINT N'PASS: SourceAuthor nvarchar(200)'; ELSE BEGIN PRINT N'FAIL: SourceAuthor'; SET @ok = 0; END
IF COL_LENGTH(N'dbo.WebsitePosts', N'SourcePublishedOn') = 3 PRINT N'PASS: SourcePublishedOn date'; ELSE BEGIN PRINT N'FAIL: SourcePublishedOn'; SET @ok = 0; END
IF OBJECT_ID(N'dbo.CK_WebsitePosts_SourceKind', N'C') IS NOT NULL PRINT N'PASS: CK_WebsitePosts_SourceKind'; ELSE BEGIN PRINT N'FAIL: CK_WebsitePosts_SourceKind'; SET @ok = 0; END
PRINT N'VERIFY_STATUS=' + CASE WHEN @ok = 1 THEN N'PASS' ELSE N'FAIL' END;
