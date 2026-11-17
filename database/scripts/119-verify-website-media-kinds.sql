-- WEBSITE-TA - READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/066-website-media-kinds.sql. Expected last line: VERIFY_STATUS=PASS (NOT_APPLIED before the migration).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
DECLARE @col bit = CASE WHEN COL_LENGTH(N'dbo.WebsiteMedia', N'ExternalUrl') IS NULL THEN 0 ELSE 1 END;
DECLARE @type bit = CASE WHEN EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_WMd_Type' AND parent_object_id = OBJECT_ID(N'dbo.WebsiteMedia')
    AND definition LIKE N'%application/pdf%' AND definition LIKE N'%video/mp4%' AND definition LIKE N'%video/youtube%' AND definition LIKE N'%image/gif%') THEN 1 ELSE 0 END;
DECLARE @size bit = CASE WHEN EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_WMd_Size' AND parent_object_id = OBJECT_ID(N'dbo.WebsiteMedia')
    AND definition LIKE N'%ExternalUrl%' AND definition LIKE N'%26214400%' AND definition LIKE N'%10485760%' AND definition LIKE N'%5242880%') THEN 1 ELSE 0 END;
IF @col = 0 AND @type = 0 AND @size = 0
BEGIN PRINT N'INFO: media kinds not present'; PRINT N'VERIFY_STATUS=NOT_APPLIED'; RETURN; END
DECLARE @ok bit = 1;
IF COL_LENGTH(N'dbo.WebsiteMedia', N'ExternalUrl') = 1000 PRINT N'PASS: ExternalUrl nvarchar(500)'; ELSE BEGIN PRINT N'FAIL: ExternalUrl'; SET @ok = 0; END
IF @type = 1 PRINT N'PASS: CK_WMd_Type allows video/mp4, video/youtube, application/pdf'; ELSE BEGIN PRINT N'FAIL: CK_WMd_Type'; SET @ok = 0; END
IF @size = 1 PRINT N'PASS: CK_WMd_Size per-kind limits'; ELSE BEGIN PRINT N'FAIL: CK_WMd_Size'; SET @ok = 0; END
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.WebsiteMedia') AND name IN (N'CK_WMd_Type', N'CK_WMd_Size') AND is_not_trusted = 1)
BEGIN PRINT N'FAIL: media constraint not trusted'; SET @ok = 0; END
ELSE PRINT N'PASS: media constraints trusted';
PRINT N'VERIFY_STATUS=' + CASE WHEN @ok = 1 THEN N'PASS' ELSE N'FAIL' END;
