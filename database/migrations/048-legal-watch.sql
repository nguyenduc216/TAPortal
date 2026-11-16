-- LEGAL-WATCH - monthly tax-law watch for the law library (operator 2026-11-14, option A): the portal reads the Công báo
-- feed of newly issued documents every week and checks the effect status of the library's documents on vbpl.vn every
-- month. New tax documents and status changes wait in a review queue; nothing enters the library without a person.
-- MIGRATION_NOT_EXECUTED.
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, after 044 and 046.
--   dbo.LegalWatchRuns   one collection run (WEEKLY = Công báo feed, MONTHLY = vbpl status check + monthly notice,
--                        MANUAL = "Kiểm tra ngay"); UX on (Kind, Period) so several app instances never run a period twice.
--   dbo.LegalWatchItems  one queue entry: a NEW document found on Công báo (unique per document number) or a STATUS change
--                        of a library document found on vbpl.vn (unique per document and status); PENDING until decided.
--   dbo.LegalDocuments   + SourceUrl (its vbpl.vn page), EffectStatus (status read there), StatusCheckedAt.
-- New tables and nullable columns only; no existing row is changed. Idempotent.
-- Verify: database/scripts/085-verify-legal-watch.sql. Rollback: database/scripts/086-rollback-legal-watch.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.LegalDocuments', N'U') IS NULL OR OBJECT_ID(N'dbo.LegalDocumentFiles', N'U') IS NULL OR OBJECT_ID(N'dbo.Users', N'U') IS NULL
 THROW 51001, 'Precondition failed: dbo.LegalDocuments, dbo.LegalDocumentFiles or dbo.Users missing (run 044 and 046 first)', 1;

IF OBJECT_ID(N'dbo.LegalWatchRuns', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.LegalWatchRuns (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_LegalWatchRuns PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        Kind varchar(10) NOT NULL,
        Period varchar(20) NOT NULL,
        Status varchar(10) NOT NULL CONSTRAINT DF_LegalWatchRuns_Status DEFAULT 'RUNNING',
        StartedAt datetime2(3) NOT NULL CONSTRAINT DF_LegalWatchRuns_StartedAt DEFAULT SYSUTCDATETIME(),
        FinishedAt datetime2(3) NULL,
        Scanned int NULL,
        NewItems int NULL,
        StatusItems int NULL,
        Error nvarchar(1000) NULL,
        NotifiedAt datetime2(3) NULL,
        RequestedBy uniqueidentifier NULL,
        CONSTRAINT FK_LegalWatchRuns_RequestedBy FOREIGN KEY (RequestedBy) REFERENCES dbo.Users(Id),
        CONSTRAINT CK_LegalWatchRuns_Kind CHECK (Kind IN ('WEEKLY','MONTHLY','MANUAL')),
        CONSTRAINT CK_LegalWatchRuns_Status CHECK (Status IN ('RUNNING','DONE','FAILED'))
    );
    CREATE UNIQUE INDEX UX_LegalWatchRuns_Period ON dbo.LegalWatchRuns(Kind, Period);
    PRINT N'dbo.LegalWatchRuns: created';
END

IF OBJECT_ID(N'dbo.LegalWatchItems', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.LegalWatchItems (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_LegalWatchItems PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        Kind varchar(10) NOT NULL,
        Source varchar(10) NOT NULL,
        SourceUrl nvarchar(500) NOT NULL,
        FileUrl nvarchar(1000) NULL,
        DocumentNo nvarchar(50) NOT NULL,
        DocType varchar(10) NOT NULL,
        TypeName nvarchar(100) NULL,
        Summary nvarchar(1000) NOT NULL,
        Agency nvarchar(200) NULL,
        IssuedDate date NULL,
        EffectiveDate date NULL,
        ExpiredDate date NULL,
        EffectStatus nvarchar(100) NULL,
        Relations nvarchar(1000) NULL,
        Keywords nvarchar(200) NULL,
        LegalDocumentId uniqueidentifier NULL,
        Status varchar(10) NOT NULL CONSTRAINT DF_LegalWatchItems_Status DEFAULT 'PENDING',
        RunId uniqueidentifier NULL,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_LegalWatchItems_CreatedAt DEFAULT SYSUTCDATETIME(),
        DecidedAt datetime2(3) NULL,
        DecidedBy uniqueidentifier NULL,
        DecisionNote nvarchar(500) NULL,
        CONSTRAINT FK_LegalWatchItems_Document FOREIGN KEY (LegalDocumentId) REFERENCES dbo.LegalDocuments(Id),
        CONSTRAINT FK_LegalWatchItems_Run FOREIGN KEY (RunId) REFERENCES dbo.LegalWatchRuns(Id),
        CONSTRAINT FK_LegalWatchItems_DecidedBy FOREIGN KEY (DecidedBy) REFERENCES dbo.Users(Id),
        CONSTRAINT CK_LegalWatchItems_Kind CHECK (Kind IN ('NEW','STATUS')),
        CONSTRAINT CK_LegalWatchItems_Source CHECK (Source IN ('CONGBAO','VBPL')),
        CONSTRAINT CK_LegalWatchItems_Type CHECK (DocType IN ('CODE','LAW','DECREE','CIRCULAR','OTHER')),
        CONSTRAINT CK_LegalWatchItems_Status CHECK (Status IN ('PENDING','IMPORTED','APPLIED','DISMISSED')),
        CONSTRAINT CK_LegalWatchItems_StatusDoc CHECK (Kind = 'NEW' OR LegalDocumentId IS NOT NULL)
    );
    CREATE UNIQUE INDEX UX_LegalWatchItems_New ON dbo.LegalWatchItems(DocumentNo) WHERE Kind = 'NEW';
    CREATE UNIQUE INDEX UX_LegalWatchItems_Status ON dbo.LegalWatchItems(LegalDocumentId, EffectStatus, ExpiredDate) WHERE Kind = 'STATUS';
    CREATE INDEX IX_LegalWatchItems_Queue ON dbo.LegalWatchItems(Status, CreatedAt DESC);
    PRINT N'dbo.LegalWatchItems: created';
END

IF COL_LENGTH(N'dbo.LegalDocuments', N'SourceUrl') IS NULL
BEGIN
    ALTER TABLE dbo.LegalDocuments ADD SourceUrl nvarchar(500) NULL, EffectStatus nvarchar(100) NULL, StatusCheckedAt datetime2(3) NULL;
    PRINT N'dbo.LegalDocuments: SourceUrl, EffectStatus, StatusCheckedAt added';
END
COMMIT TRANSACTION;
PRINT N'048-legal-watch.sql: OK';
