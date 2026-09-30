/* TAPortal 024 - Provider webhook/outbox retry and processing lease. */
USE [TAPortal];
GO
IF COL_LENGTH('dbo.BankWebhookEvents','AttemptCount') IS NULL ALTER TABLE dbo.BankWebhookEvents ADD AttemptCount int NOT NULL CONSTRAINT DF_BWE_Attempts DEFAULT 0;
IF COL_LENGTH('dbo.BankWebhookEvents','NextAttemptAt') IS NULL ALTER TABLE dbo.BankWebhookEvents ADD NextAttemptAt datetime2(3) NULL;
IF COL_LENGTH('dbo.BankWebhookEvents','LockedUntil') IS NULL ALTER TABLE dbo.BankWebhookEvents ADD LockedUntil datetime2(3) NULL;
IF COL_LENGTH('dbo.BankWebhookEvents','LockId') IS NULL ALTER TABLE dbo.BankWebhookEvents ADD LockId uniqueidentifier NULL;
GO
IF COL_LENGTH('dbo.OutboxMessages','LockedUntil') IS NULL ALTER TABLE dbo.OutboxMessages ADD LockedUntil datetime2(3) NULL;
IF COL_LENGTH('dbo.OutboxMessages','LockId') IS NULL ALTER TABLE dbo.OutboxMessages ADD LockId uniqueidentifier NULL;
IF COL_LENGTH('dbo.OutboxMessages','Priority') IS NULL ALTER TABLE dbo.OutboxMessages ADD Priority tinyint NOT NULL CONSTRAINT DF_Outbox_Priority DEFAULT 5;
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.BankWebhookEvents') AND name='IX_BWE_WorkQueue') CREATE INDEX IX_BWE_WorkQueue ON dbo.BankWebhookEvents(ProcessingStatus,NextAttemptAt,ReceivedAt) INCLUDE(AttemptCount,LockedUntil);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.OutboxMessages') AND name='IX_Outbox_WorkQueue') CREATE INDEX IX_Outbox_WorkQueue ON dbo.OutboxMessages(Status,NextAttemptAt,Priority,CreatedAt) INCLUDE(AttemptCount,LockedUntil,EventType);
GO
IF OBJECT_ID(N'dbo.ProviderDeadLetters',N'U') IS NULL BEGIN
 CREATE TABLE dbo.ProviderDeadLetters(Id bigint IDENTITY(1,1) CONSTRAINT PK_ProviderDeadLetters PRIMARY KEY,PartnerId uniqueidentifier NULL,ProviderId uniqueidentifier NULL,SourceType varchar(30) NOT NULL,SourceId nvarchar(200) NOT NULL,Operation varchar(100) NULL,PayloadSanitized nvarchar(max) NULL,ErrorCode nvarchar(100) NULL,ErrorMessage nvarchar(2000) NULL,AttemptCount int NOT NULL,FailedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),ResolvedAt datetime2(3) NULL,ResolvedBy uniqueidentifier NULL,ResolutionNote nvarchar(1000) NULL,CONSTRAINT FK_PDL_Partner FOREIGN KEY(PartnerId) REFERENCES dbo.Partners(Id),CONSTRAINT FK_PDL_Provider FOREIGN KEY(ProviderId) REFERENCES dbo.Providers(Id),CONSTRAINT CK_PDL_Source CHECK(SourceType IN('WEBHOOK','OUTBOX','API','RECONCILIATION')));
 CREATE INDEX IX_PDL_Open ON dbo.ProviderDeadLetters(FailedAt DESC) WHERE ResolvedAt IS NULL;
END
GO
PRINT '024-provider-processing-retry.sql: OK';
GO