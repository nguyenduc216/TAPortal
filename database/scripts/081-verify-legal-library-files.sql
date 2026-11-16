-- CONTRACT-REVIEW - READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/046-legal-library-files.sql. Expected last line: VERIFY_STATUS=PASS (NOT_APPLIED before the migration).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
IF OBJECT_ID(N'dbo.LegalDocumentFiles', N'U') IS NULL BEGIN PRINT N'INFO: dbo.LegalDocumentFiles missing'; PRINT N'VERIFY_STATUS=NOT_APPLIED'; RETURN; END
DECLARE @fail int = 0;
IF COL_LENGTH(N'dbo.LegalDocumentFiles', N'IsCurrent') IS NULL OR COL_LENGTH(N'dbo.LegalDocumentFiles', N'ArticleCount') IS NULL
    BEGIN PRINT N'FAIL: LegalDocumentFiles columns missing'; SET @fail += 1; END
IF OBJECT_ID(N'dbo.CK_LegalDocumentFiles_Size', N'C') IS NULL BEGIN PRINT N'FAIL: CK_LegalDocumentFiles_Size missing'; SET @fail += 1; END
IF COL_LENGTH(N'dbo.LegalArticles', N'UpdatedAt') IS NULL OR COL_LENGTH(N'dbo.LegalArticles', N'UpdatedBy') IS NULL
    BEGIN PRINT N'FAIL: LegalArticles.UpdatedAt/UpdatedBy missing'; SET @fail += 1; END
IF @fail = 0 PRINT N'PASS: law library files and article edit columns';
PRINT N'VERIFY_STATUS=' + CASE WHEN @fail = 0 THEN N'PASS' ELSE N'FAIL' END;
