-- CONTRACT-REVIEW - rollback of migrations/046-legal-library-files.sql.
-- Drops dbo.LegalDocumentFiles and the UpdatedAt/UpdatedBy columns of dbo.LegalArticles. Refuses (THROW 51002) while any
-- uploaded law file exists. The articles themselves (dbo.LegalArticles of 044) are kept.
-- After it, database/scripts/081-verify-legal-library-files.sql must print VERIFY_STATUS=NOT_APPLIED.
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @files int = 0;
IF OBJECT_ID(N'dbo.LegalDocumentFiles', N'U') IS NOT NULL EXEC sp_executesql N'SELECT @n = COUNT(1) FROM dbo.LegalDocumentFiles;', N'@n int OUTPUT', @n = @files OUTPUT;
IF @files > 0
 THROW 51002, 'Rollback refused: dbo.LegalDocumentFiles contains rows; no changes executed', 1;
IF OBJECT_ID(N'dbo.LegalDocumentFiles', N'U') IS NOT NULL BEGIN DROP TABLE dbo.LegalDocumentFiles; PRINT N'dbo.LegalDocumentFiles: dropped'; END
IF COL_LENGTH(N'dbo.LegalArticles', N'UpdatedAt') IS NOT NULL
BEGIN
    EXEC(N'ALTER TABLE dbo.LegalArticles DROP COLUMN UpdatedAt, UpdatedBy;');
    PRINT N'dbo.LegalArticles: UpdatedAt, UpdatedBy dropped';
END
COMMIT TRANSACTION;
PRINT N'082-rollback-legal-library-files: done';
