-- CONTRACT-REVIEW - "Hợp đồng dịch vụ" phase 1 (operator 2026-11-14): a contract revision request per customer. Staff upload
-- the customer's DOCX, an AI reviewer (Claude API) flags clauses for party A, B or both with the legal basis and a proposed
-- wording, staff accept, edit or reject each finding, and every round becomes a new version; the original is never changed.
-- MIGRATION_NOT_EXECUTED.
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, after 043.
--   dbo.ContractReviews          one revision request: code CLnnnnnn, customer, optional contract, party reviewed, status.
--   dbo.ContractReviewFiles      uploaded original and exported DOCX files (varbinary, like dbo.CustomerContractFiles).
--   dbo.ContractReviewVersions   version 0 = original; each later version is a full snapshot.
--   dbo.ContractReviewParagraphs the paragraphs of one version; ParagraphKey stays the same across versions.
--   dbo.ContractReviewRuns       one AI review run: model, tokens, cost, status, raw JSON answer.
--   dbo.ContractReviewFindings   one flagged clause: quote, issue, severity, party, legal basis, proposal, staff decision.
--   dbo.ContractReviewChanges    one changed paragraph between two versions, with the customer's answer.
--   dbo.LegalDocuments / dbo.LegalArticles  law library the AI may cite; citations outside it are marked unverified.
--   dbo.NumberSequences          + CONTRACT_REVIEW_CODE (prefix CL, 6 digits).
--   dbo.Permissions              + CONTRACTS.REVIEW.* and LEGAL.LIBRARY.MANAGE, granted to SYS_ADMIN.
-- Rows it inserts carry CreatedBy = 'A0000044-0000-4000-8000-000000000044' (marker) so the rollback removes exactly them.
-- New tables and reference rows only; no existing row is changed. Idempotent.
-- Verify: database/scripts/077-verify-contract-review.sql. Rollback: database/scripts/078-rollback-contract-review.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL OR OBJECT_ID(N'dbo.Customers', N'U') IS NULL OR OBJECT_ID(N'dbo.CustomerContracts', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Permissions', N'U') IS NULL OR OBJECT_ID(N'dbo.RolePermissions', N'U') IS NULL OR OBJECT_ID(N'dbo.Roles', N'U') IS NULL
   OR OBJECT_ID(N'dbo.NumberSequences', N'U') IS NULL
 THROW 51001, 'Precondition failed: dbo.Users, dbo.Customers, dbo.CustomerContracts, dbo.Permissions, dbo.RolePermissions, dbo.Roles or dbo.NumberSequences missing', 1;
DECLARE @SysAdmin uniqueidentifier = (SELECT TOP 1 Id FROM dbo.Roles WHERE Code = 'SYS_ADMIN' AND IsDeleted = 0);
IF @SysAdmin IS NULL
 THROW 51001, 'Precondition failed: role SYS_ADMIN missing', 1;
DECLARE @Marker uniqueidentifier = 'A0000044-0000-4000-8000-000000000044';

IF OBJECT_ID(N'dbo.ContractReviews', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ContractReviews (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_ContractReviews PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        Code varchar(20) NOT NULL,
        CustomerId uniqueidentifier NOT NULL,
        ContractId uniqueidentifier NULL,
        Title nvarchar(250) NOT NULL,
        CustomerRole varchar(10) NOT NULL,
        Status varchar(20) NOT NULL CONSTRAINT DF_ContractReviews_Status DEFAULT 'NEW',
        CurrentVersionNo int NOT NULL CONSTRAINT DF_ContractReviews_Version DEFAULT 0,
        FinalVersionNo int NULL,
        Note nvarchar(2000) NULL,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_ContractReviews_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NOT NULL,
        UpdatedAt datetime2(3) NULL,
        UpdatedBy uniqueidentifier NULL,
        IsDeleted bit NOT NULL CONSTRAINT DF_ContractReviews_IsDeleted DEFAULT 0,
        DeletedAt datetime2(3) NULL,
        DeletedBy uniqueidentifier NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT FK_ContractReviews_Customer FOREIGN KEY (CustomerId) REFERENCES dbo.Customers(Id),
        CONSTRAINT FK_ContractReviews_Contract FOREIGN KEY (ContractId) REFERENCES dbo.CustomerContracts(Id),
        CONSTRAINT FK_ContractReviews_CreatedBy FOREIGN KEY (CreatedBy) REFERENCES dbo.Users(Id),
        CONSTRAINT CK_ContractReviews_Role CHECK (CustomerRole IN ('A','B','BOTH')),
        CONSTRAINT CK_ContractReviews_Status CHECK (Status IN ('NEW','ANALYZING','IN_REVIEW','DRAFT_READY','SENT','CUSTOMER_REPLIED','FINALIZED','CANCELLED'))
    );
    CREATE UNIQUE INDEX UX_ContractReviews_Code ON dbo.ContractReviews(Code);
    CREATE INDEX IX_ContractReviews_Customer ON dbo.ContractReviews(CustomerId, CreatedAt DESC) WHERE IsDeleted = 0;
    PRINT N'dbo.ContractReviews: created';
END

IF OBJECT_ID(N'dbo.ContractReviewFiles', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ContractReviewFiles (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_ContractReviewFiles PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        ReviewId uniqueidentifier NOT NULL,
        VersionNo int NOT NULL,
        Kind varchar(10) NOT NULL,
        FileName nvarchar(255) NOT NULL,
        ContentType varchar(150) NOT NULL,
        SizeBytes int NOT NULL,
        Data varbinary(max) NOT NULL,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_ContractReviewFiles_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NOT NULL,
        CONSTRAINT FK_ContractReviewFiles_Review FOREIGN KEY (ReviewId) REFERENCES dbo.ContractReviews(Id),
        CONSTRAINT CK_ContractReviewFiles_Kind CHECK (Kind IN ('ORIGINAL','EXPORT')),
        CONSTRAINT CK_ContractReviewFiles_Size CHECK (SizeBytes > 0 AND SizeBytes <= 10485760)
    );
    CREATE INDEX IX_ContractReviewFiles_Review ON dbo.ContractReviewFiles(ReviewId, CreatedAt DESC);
    PRINT N'dbo.ContractReviewFiles: created';
END

IF OBJECT_ID(N'dbo.ContractReviewVersions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ContractReviewVersions (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_ContractReviewVersions PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        ReviewId uniqueidentifier NOT NULL,
        VersionNo int NOT NULL,
        Source varchar(10) NOT NULL,
        Label nvarchar(200) NOT NULL,
        Note nvarchar(2000) NULL,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_ContractReviewVersions_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NOT NULL,
        CONSTRAINT FK_ContractReviewVersions_Review FOREIGN KEY (ReviewId) REFERENCES dbo.ContractReviews(Id),
        CONSTRAINT CK_ContractReviewVersions_Source CHECK (Source IN ('ORIGINAL','STAFF','CUSTOMER'))
    );
    CREATE UNIQUE INDEX UX_ContractReviewVersions_No ON dbo.ContractReviewVersions(ReviewId, VersionNo);
    PRINT N'dbo.ContractReviewVersions: created';
END

IF OBJECT_ID(N'dbo.ContractReviewParagraphs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ContractReviewParagraphs (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ContractReviewParagraphs PRIMARY KEY,
        VersionId uniqueidentifier NOT NULL,
        Seq int NOT NULL,
        ParagraphKey varchar(20) NOT NULL,
        Kind varchar(10) NOT NULL,
        Text nvarchar(max) NOT NULL,
        CONSTRAINT FK_ContractReviewParagraphs_Version FOREIGN KEY (VersionId) REFERENCES dbo.ContractReviewVersions(Id),
        CONSTRAINT CK_ContractReviewParagraphs_Kind CHECK (Kind IN ('HEADING','TEXT','LIST','TABLE'))
    );
    CREATE UNIQUE INDEX UX_ContractReviewParagraphs_Seq ON dbo.ContractReviewParagraphs(VersionId, Seq);
    CREATE UNIQUE INDEX UX_ContractReviewParagraphs_Key ON dbo.ContractReviewParagraphs(VersionId, ParagraphKey);
    PRINT N'dbo.ContractReviewParagraphs: created';
END

IF OBJECT_ID(N'dbo.ContractReviewRuns', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ContractReviewRuns (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_ContractReviewRuns PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        ReviewId uniqueidentifier NOT NULL,
        VersionNo int NOT NULL,
        Status varchar(10) NOT NULL CONSTRAINT DF_ContractReviewRuns_Status DEFAULT 'QUEUED',
        MaskPersonalData bit NOT NULL CONSTRAINT DF_ContractReviewRuns_Mask DEFAULT 1,
        Model varchar(60) NULL,
        InputTokens int NULL,
        OutputTokens int NULL,
        CostUsd decimal(10,4) NULL,
        FindingCount int NULL,
        Summary nvarchar(max) NULL,
        Error nvarchar(1000) NULL,
        ResponseJson nvarchar(max) NULL,
        RequestedAt datetime2(3) NOT NULL CONSTRAINT DF_ContractReviewRuns_RequestedAt DEFAULT SYSUTCDATETIME(),
        RequestedBy uniqueidentifier NOT NULL,
        StartedAt datetime2(3) NULL,
        FinishedAt datetime2(3) NULL,
        CONSTRAINT FK_ContractReviewRuns_Review FOREIGN KEY (ReviewId) REFERENCES dbo.ContractReviews(Id),
        CONSTRAINT CK_ContractReviewRuns_Status CHECK (Status IN ('QUEUED','RUNNING','DONE','FAILED'))
    );
    CREATE INDEX IX_ContractReviewRuns_Review ON dbo.ContractReviewRuns(ReviewId, RequestedAt DESC);
    CREATE INDEX IX_ContractReviewRuns_Queue ON dbo.ContractReviewRuns(Status, RequestedAt) WHERE Status IN ('QUEUED','RUNNING');
    PRINT N'dbo.ContractReviewRuns: created';
END

IF OBJECT_ID(N'dbo.ContractReviewFindings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ContractReviewFindings (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_ContractReviewFindings PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        ReviewId uniqueidentifier NOT NULL,
        RunId uniqueidentifier NULL,
        VersionNo int NOT NULL,
        ParagraphKey varchar(20) NOT NULL,
        Action varchar(10) NOT NULL,
        Quote nvarchar(max) NULL,
        Issue nvarchar(max) NOT NULL,
        Severity varchar(10) NOT NULL,
        Party varchar(10) NOT NULL,
        LegalBasis nvarchar(max) NULL,
        LegalVerified bit NOT NULL CONSTRAINT DF_ContractReviewFindings_Verified DEFAULT 0,
        Suggestion nvarchar(max) NULL,
        RevisedText nvarchar(max) NULL,
        Status varchar(10) NOT NULL CONSTRAINT DF_ContractReviewFindings_Status DEFAULT 'OPEN',
        StaffText nvarchar(max) NULL,
        StaffNote nvarchar(2000) NULL,
        Source varchar(10) NOT NULL,
        AppliedInVersionNo int NULL,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_ContractReviewFindings_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NOT NULL,
        DecidedAt datetime2(3) NULL,
        DecidedBy uniqueidentifier NULL,
        CONSTRAINT FK_ContractReviewFindings_Review FOREIGN KEY (ReviewId) REFERENCES dbo.ContractReviews(Id),
        CONSTRAINT FK_ContractReviewFindings_Run FOREIGN KEY (RunId) REFERENCES dbo.ContractReviewRuns(Id),
        CONSTRAINT CK_ContractReviewFindings_Action CHECK (Action IN ('REPLACE','INSERT','DELETE','NOTE')),
        CONSTRAINT CK_ContractReviewFindings_Severity CHECK (Severity IN ('HIGH','MEDIUM','LOW')),
        CONSTRAINT CK_ContractReviewFindings_Party CHECK (Party IN ('A','B','BOTH')),
        CONSTRAINT CK_ContractReviewFindings_Status CHECK (Status IN ('OPEN','ACCEPTED','EDITED','REJECTED')),
        CONSTRAINT CK_ContractReviewFindings_Source CHECK (Source IN ('AI','STAFF'))
    );
    CREATE INDEX IX_ContractReviewFindings_Review ON dbo.ContractReviewFindings(ReviewId, VersionNo, ParagraphKey);
    PRINT N'dbo.ContractReviewFindings: created';
END

IF OBJECT_ID(N'dbo.ContractReviewChanges', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ContractReviewChanges (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_ContractReviewChanges PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        ReviewId uniqueidentifier NOT NULL,
        FromVersionNo int NOT NULL,
        ToVersionNo int NOT NULL,
        ParagraphKey varchar(20) NOT NULL,
        FindingId uniqueidentifier NULL,
        OldText nvarchar(max) NULL,
        NewText nvarchar(max) NULL,
        Note nvarchar(max) NULL,
        CustomerDecision varchar(10) NOT NULL CONSTRAINT DF_ContractReviewChanges_Decision DEFAULT 'PENDING',
        CustomerText nvarchar(max) NULL,
        CustomerNote nvarchar(2000) NULL,
        DecidedAt datetime2(3) NULL,
        DecidedBy uniqueidentifier NULL,
        CONSTRAINT FK_ContractReviewChanges_Review FOREIGN KEY (ReviewId) REFERENCES dbo.ContractReviews(Id),
        CONSTRAINT FK_ContractReviewChanges_Finding FOREIGN KEY (FindingId) REFERENCES dbo.ContractReviewFindings(Id),
        CONSTRAINT CK_ContractReviewChanges_Decision CHECK (CustomerDecision IN ('PENDING','AGREED','DECLINED','COUNTER'))
    );
    CREATE INDEX IX_ContractReviewChanges_Review ON dbo.ContractReviewChanges(ReviewId, ToVersionNo);
    PRINT N'dbo.ContractReviewChanges: created';
END

IF OBJECT_ID(N'dbo.LegalDocuments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.LegalDocuments (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_LegalDocuments PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        DocumentNo nvarchar(50) NOT NULL,
        Title nvarchar(300) NOT NULL,
        DocType varchar(10) NOT NULL,
        IssuedDate date NULL,
        EffectiveDate date NULL,
        ExpiredDate date NULL,
        Note nvarchar(1000) NULL,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_LegalDocuments_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NOT NULL,
        UpdatedAt datetime2(3) NULL,
        UpdatedBy uniqueidentifier NULL,
        IsDeleted bit NOT NULL CONSTRAINT DF_LegalDocuments_IsDeleted DEFAULT 0,
        DeletedAt datetime2(3) NULL,
        DeletedBy uniqueidentifier NULL,
        CONSTRAINT CK_LegalDocuments_Type CHECK (DocType IN ('CODE','LAW','DECREE','CIRCULAR','OTHER'))
    );
    CREATE UNIQUE INDEX UX_LegalDocuments_No ON dbo.LegalDocuments(DocumentNo) WHERE IsDeleted = 0;
    PRINT N'dbo.LegalDocuments: created';
END

IF OBJECT_ID(N'dbo.LegalArticles', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.LegalArticles (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_LegalArticles PRIMARY KEY,
        DocumentId uniqueidentifier NOT NULL,
        Seq int NOT NULL,
        ArticleNo nvarchar(20) NOT NULL,
        Title nvarchar(500) NULL,
        Content nvarchar(max) NOT NULL,
        CONSTRAINT FK_LegalArticles_Document FOREIGN KEY (DocumentId) REFERENCES dbo.LegalDocuments(Id)
    );
    CREATE UNIQUE INDEX UX_LegalArticles_No ON dbo.LegalArticles(DocumentId, ArticleNo);
    PRINT N'dbo.LegalArticles: created';
END

IF NOT EXISTS (SELECT 1 FROM dbo.NumberSequences WHERE Code = 'CONTRACT_REVIEW_CODE')
    INSERT dbo.NumberSequences (Id, Code, ScopeType, ScopeId, Prefix, Suffix, CurrentValue, Padding, ResetPolicy, IsActive)
    SELECT NEWID(), 'CONTRACT_REVIEW_CODE', s.ScopeType, NULL, N'CL', NULL, 0, 6, s.ResetPolicy, 1
    FROM (SELECT TOP 1 ScopeType, ResetPolicy FROM dbo.NumberSequences WHERE Code = 'EMPLOYEE_CODE') s;
IF NOT EXISTS (SELECT 1 FROM dbo.NumberSequences WHERE Code = 'CONTRACT_REVIEW_CODE')
 THROW 51001, 'Precondition failed: NumberSequences EMPLOYEE_CODE missing (used as the template for CONTRACT_REVIEW_CODE)', 1;

DECLARE @Catalogue TABLE (Code varchar(200) COLLATE DATABASE_DEFAULT NOT NULL PRIMARY KEY, Name nvarchar(250) NOT NULL, ModuleCode varchar(100) NOT NULL, FunctionCode varchar(100) NOT NULL, ActionCode varchar(50) NOT NULL);
INSERT @Catalogue (Code, Name, ModuleCode, FunctionCode, ActionCode) VALUES
 ('CONTRACTS.REVIEW.VIEW',     N'View contract revision requests, versions and findings',       'CONTRACT_REVIEW', 'REVIEW',  'VIEW'),
 ('CONTRACTS.REVIEW.CREATE',   N'Create contract revision requests and upload the original',    'CONTRACT_REVIEW', 'REVIEW',  'CREATE'),
 ('CONTRACTS.REVIEW.RUN_AI',   N'Run the AI contract review (paid API usage)',                  'CONTRACT_REVIEW', 'REVIEW',  'RUN_AI'),
 ('CONTRACTS.REVIEW.EDIT',     N'Decide findings, create versions, record customer answers',   'CONTRACT_REVIEW', 'REVIEW',  'EDIT'),
 ('CONTRACTS.REVIEW.FINALIZE', N'Approve a version as final or cancel a revision request',      'CONTRACT_REVIEW', 'REVIEW',  'FINALIZE'),
 ('LEGAL.LIBRARY.MANAGE',      N'Manage the law library used for legal citations',              'CONTRACT_REVIEW', 'LIBRARY', 'MANAGE');
INSERT dbo.Permissions (Id, Code, Name, ModuleCode, FunctionCode, ActionCode, Description, IsSystem, IsActive, CreatedBy)
SELECT NEWID(), c.Code, c.Name, c.ModuleCode, c.FunctionCode, c.ActionCode, N'TAPortal Admin (migration 044)', 1, 1, @Marker
FROM @Catalogue c
WHERE NOT EXISTS (SELECT 1 FROM dbo.Permissions p WHERE p.Code = c.Code AND p.IsDeleted = 0);
INSERT dbo.RolePermissions (RoleId, PermissionId, CreatedBy)
SELECT @SysAdmin, p.Id, @Marker
FROM @Catalogue c JOIN dbo.Permissions p ON p.Code = c.Code AND p.IsDeleted = 0
WHERE NOT EXISTS (SELECT 1 FROM dbo.RolePermissions rp WHERE rp.RoleId = @SysAdmin AND rp.PermissionId = p.Id);
PRINT N'INFO: SYS_ADMIN grants inserted = ' + CAST(@@ROWCOUNT AS nvarchar(10));
COMMIT TRANSACTION;
PRINT N'044-contract-review.sql: OK';
