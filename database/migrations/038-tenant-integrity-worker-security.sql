/* RC2 038 - Composite tenant integrity + worker inbox/lease/security metadata. */
USE [TAPortal];
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.Companies') AND name='UX_Companies_PartnerId_Id') CREATE UNIQUE INDEX UX_Companies_PartnerId_Id ON dbo.Companies(PartnerId,Id) WHERE PartnerId IS NOT NULL;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.Customers') AND name='UX_Customers_PartnerId_Id') CREATE UNIQUE INDEX UX_Customers_PartnerId_Id ON dbo.Customers(PartnerId,Id) WHERE PartnerId IS NOT NULL;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.PaymentRequests') AND name='UX_PReq_PartnerId_Id') CREATE UNIQUE INDEX UX_PReq_PartnerId_Id ON dbo.PaymentRequests(PartnerId,Id);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.PaymentTransactions') AND name='UX_PT_PartnerId_Id') CREATE UNIQUE INDEX UX_PT_PartnerId_Id ON dbo.PaymentTransactions(PartnerId,Id);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.InvoiceRequests') AND name='UX_IReq_PartnerId_Id') CREATE UNIQUE INDEX UX_IReq_PartnerId_Id ON dbo.InvoiceRequests(PartnerId,Id);
GO
IF COL_LENGTH('dbo.PaymentAllocations','PartnerId') IS NULL ALTER TABLE dbo.PaymentAllocations ADD PartnerId uniqueidentifier NULL;
GO
UPDATE a SET PartnerId=r.PartnerId FROM dbo.PaymentAllocations a JOIN dbo.PaymentRequests r ON r.Id=a.PaymentRequestId WHERE a.PartnerId IS NULL;
IF EXISTS(SELECT 1 FROM dbo.PaymentAllocations WHERE PartnerId IS NULL) THROW 51380,'PaymentAllocation tenant backfill failed.',1;
ALTER TABLE dbo.PaymentAllocations ALTER COLUMN PartnerId uniqueidentifier NOT NULL;
GO
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name='FK_PA_Partner') ALTER TABLE dbo.PaymentAllocations ADD CONSTRAINT FK_PA_Partner FOREIGN KEY(PartnerId) REFERENCES dbo.Partners(Id);
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name='FK_PA_TenantRequest') ALTER TABLE dbo.PaymentAllocations ADD CONSTRAINT FK_PA_TenantRequest FOREIGN KEY(PartnerId,PaymentRequestId) REFERENCES dbo.PaymentRequests(PartnerId,Id);
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name='FK_PA_TenantTransaction') ALTER TABLE dbo.PaymentAllocations ADD CONSTRAINT FK_PA_TenantTransaction FOREIGN KEY(PartnerId,PaymentTransactionId) REFERENCES dbo.PaymentTransactions(PartnerId,Id);
GO
IF OBJECT_ID(N'dbo.InboxMessages',N'U') IS NULL BEGIN
 CREATE TABLE dbo.InboxMessages(Id uniqueidentifier NOT NULL DEFAULT NEWSEQUENTIALID() CONSTRAINT PK_InboxMessages PRIMARY KEY,PartnerId uniqueidentifier NULL,ProviderId uniqueidentifier NULL,MessageType varchar(100) NOT NULL,ExternalMessageId nvarchar(250) NOT NULL,PayloadHash varchar(128) NULL,Status varchar(20) NOT NULL DEFAULT 'RECEIVED',AttemptCount int NOT NULL DEFAULT 0,LockedUntil datetime2(3) NULL,LockId uniqueidentifier NULL,ReceivedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),ProcessedAt datetime2(3) NULL,LastError nvarchar(2000) NULL,CONSTRAINT FK_Inbox_Partner FOREIGN KEY(PartnerId) REFERENCES dbo.Partners(Id),CONSTRAINT FK_Inbox_Provider FOREIGN KEY(ProviderId) REFERENCES dbo.Providers(Id),CONSTRAINT CK_Inbox_Status CHECK(Status IN('RECEIVED','PROCESSING','PROCESSED','FAILED','DEAD')));
 CREATE UNIQUE INDEX UX_Inbox_ProviderMessage ON dbo.InboxMessages(ProviderId,MessageType,ExternalMessageId);
 CREATE INDEX IX_Inbox_Work ON dbo.InboxMessages(Status,LockedUntil,ReceivedAt);
END
GO
IF COL_LENGTH('dbo.BankLinkSessions','HostedLinkUrl') IS NOT NULL UPDATE dbo.BankLinkSessions SET HostedLinkUrl=NULL WHERE HostedLinkUrl IS NOT NULL;
GO
PRINT '038-tenant-integrity-worker-security.sql: OK';
GO