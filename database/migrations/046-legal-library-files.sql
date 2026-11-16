-- CONTRACT-REVIEW - law library files (operator 2026-11-14): upload a Word / PDF / text file of a law instead of pasting
-- it, keep every uploaded file per document (the current one and the history), re-import an older file, and edit or
-- remove single articles.
-- MIGRATION_NOT_EXECUTED.
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, after 044 (and 045).
--   dbo.LegalDocumentFiles  one uploaded source file of a law document: name, type, size, data, articles found, current flag.
--   dbo.LegalArticles       + UpdatedAt, UpdatedBy (an article edited by hand).
-- New table and nullable columns only; no existing row is changed. Idempotent.
-- Verify: database/scripts/081-verify-legal-library-files.sql. Rollback: database/scripts/082-rollback-legal-library-files.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.LegalDocuments', N'U') IS NULL OR OBJECT_ID(N'dbo.LegalArticles', N'U') IS NULL
 THROW 51001, 'Precondition failed: dbo.LegalDocuments or dbo.LegalArticles missing (run 044 first)', 1;

IF OBJECT_ID(N'dbo.LegalDocumentFiles', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.LegalDocumentFiles (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_LegalDocumentFiles PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        DocumentId uniqueidentifier NOT NULL,
        FileName nvarchar(255) NOT NULL,
        ContentType varchar(150) NOT NULL,
        SizeBytes int NOT NULL,
        Data varbinary(max) NOT NULL,
        ArticleCount int NOT NULL,
        IsCurrent bit NOT NULL CONSTRAINT DF_LegalDocumentFiles_IsCurrent DEFAULT 0,
        Note nvarchar(500) NULL,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_LegalDocumentFiles_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NOT NULL,
        CONSTRAINT FK_LegalDocumentFiles_Document FOREIGN KEY (DocumentId) REFERENCES dbo.LegalDocuments(Id),
        CONSTRAINT CK_LegalDocumentFiles_Size CHECK (SizeBytes > 0 AND SizeBytes <= 20971520)
    );
    CREATE INDEX IX_LegalDocumentFiles_Document ON dbo.LegalDocumentFiles(DocumentId, CreatedAt DESC);
    PRINT N'dbo.LegalDocumentFiles: created';
END

IF COL_LENGTH(N'dbo.LegalArticles', N'UpdatedAt') IS NULL
BEGIN
    ALTER TABLE dbo.LegalArticles ADD UpdatedAt datetime2(3) NULL, UpdatedBy uniqueidentifier NULL;
    PRINT N'dbo.LegalArticles: UpdatedAt, UpdatedBy added';
END
COMMIT TRANSACTION;
PRINT N'046-legal-library-files.sql: OK';
