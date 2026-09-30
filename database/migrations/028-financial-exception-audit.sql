/* TAPortal 028 - Financial exception/audit controls. V1 candidate. */
USE [TAPortal];
GO
IF OBJECT_ID(N'dbo.FinancialExceptions',N'U') IS NULL BEGIN
 CREATE TABLE dbo.FinancialExceptions(Id uniqueidentifier NOT NULL DEFAULT NEWSEQUENTIALID() CONSTRAINT PK_FinancialExceptions PRIMARY KEY,PartnerId uniqueidentifier NOT NULL,ExceptionType varchar(50) NOT NULL,Severity varchar(10) NOT NULL,SourceType varchar(40) NOT NULL,SourceId nvarchar(200) NOT NULL,Status varchar(20) NOT NULL DEFAULT 'OPEN',Title nvarchar(300) NOT NULL,Details nvarchar(max) NULL,DetectedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),AssignedTo uniqueidentifier NULL,ResolvedAt datetime2(3) NULL,ResolvedBy uniqueidentifier NULL,Resolution nvarchar(1000) NULL,CONSTRAINT FK_FE_Partner FOREIGN KEY(PartnerId) REFERENCES dbo.Partners(Id),CONSTRAINT CK_FE_Severity CHECK(Severity IN('INFO','WARNING','ERROR','CRITICAL')),CONSTRAINT CK_FE_Status CHECK(Status IN('OPEN','IN_REVIEW','RESOLVED','IGNORED')));
 CREATE INDEX IX_FE_Open ON dbo.FinancialExceptions(PartnerId,Severity,DetectedAt DESC) WHERE Status IN('OPEN','IN_REVIEW');
END
GO
IF OBJECT_ID(N'dbo.DataRetentionPolicies',N'U') IS NULL BEGIN
 CREATE TABLE dbo.DataRetentionPolicies(Id uniqueidentifier NOT NULL DEFAULT NEWSEQUENTIALID() CONSTRAINT PK_DataRetentionPolicies PRIMARY KEY,DataType varchar(60) NOT NULL,RetentionDays int NULL,ArchiveBeforeDelete bit NOT NULL DEFAULT 1,AllowDelete bit NOT NULL DEFAULT 0,IsActive bit NOT NULL DEFAULT 1,Notes nvarchar(500) NULL,CreatedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),UpdatedAt datetime2(3) NULL,CONSTRAINT CK_DRP_Days CHECK(RetentionDays IS NULL OR RetentionDays>0));
 CREATE UNIQUE INDEX UX_DRP_DataType ON dbo.DataRetentionPolicies(DataType);
END
GO
MERGE dbo.DataRetentionPolicies T USING(VALUES
 ('BANK_TRANSACTION',NULL,1,0,N'Financial source record; no automatic delete in V1'),
 ('PAYMENT_ALLOCATION',NULL,1,0,N'Financial allocation history; reverse instead of delete'),
 ('CREDIT_LEDGER',NULL,1,0,N'Immutable ledger; compensating entries only'),
 ('INVOICE',NULL,1,0,N'Invoice/legal record; lifecycle operations only'),
 ('WEBHOOK_EVENT',365,1,0,N'Archive policy may be implemented after V1'),
 ('PROVIDER_API_LOG',180,1,0,N'Sanitized integration log; archive before any purge')
) S(DataType,RetentionDays,ArchiveBeforeDelete,AllowDelete,Notes) ON T.DataType=S.DataType
WHEN MATCHED THEN UPDATE SET RetentionDays=S.RetentionDays,ArchiveBeforeDelete=S.ArchiveBeforeDelete,AllowDelete=S.AllowDelete,Notes=S.Notes,UpdatedAt=SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT(DataType,RetentionDays,ArchiveBeforeDelete,AllowDelete,Notes) VALUES(S.DataType,S.RetentionDays,S.ArchiveBeforeDelete,S.AllowDelete,S.Notes);
GO
IF OBJECT_ID(N'dbo.SchemaVersions',N'U') IS NULL BEGIN
 CREATE TABLE dbo.SchemaVersions(Version varchar(30) NOT NULL CONSTRAINT PK_SchemaVersions PRIMARY KEY,Status varchar(20) NOT NULL,Description nvarchar(500) NULL,AppliedAt datetime2(3) NULL,AppliedBy nvarchar(200) NULL,SourceCommit varchar(64) NULL,CreatedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),CONSTRAINT CK_SV_Status CHECK(Status IN('CANDIDATE','APPROVED','APPLIED','SUPERSEDED')));
END
GO
IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE Version='1.0.0-rc1') INSERT dbo.SchemaVersions(Version,Status,Description) VALUES('1.0.0-rc1','CANDIDATE',N'T.A Portal database schema V1 release candidate; requires ChatGPT + Codex audit before deployment bundle is frozen.');
GO
PRINT '028-financial-exception-audit.sql: OK';
GO