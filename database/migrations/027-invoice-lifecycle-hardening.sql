/* TAPortal 027 - Invoice adjustment/replacement/cancellation lifecycle. V1 candidate. */
USE [TAPortal];
GO
IF OBJECT_ID(N'dbo.InvoiceRelations',N'U') IS NULL BEGIN
 CREATE TABLE dbo.InvoiceRelations(Id uniqueidentifier NOT NULL DEFAULT NEWSEQUENTIALID() CONSTRAINT PK_InvoiceRelations PRIMARY KEY,PartnerId uniqueidentifier NOT NULL,SourceInvoiceId uniqueidentifier NOT NULL,TargetInvoiceId uniqueidentifier NULL,RelationType varchar(20) NOT NULL,Reason nvarchar(1000) NOT NULL,ExternalReference nvarchar(200) NULL,Status varchar(20) NOT NULL DEFAULT 'PENDING',RequestedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),CompletedAt datetime2(3) NULL,CreatedBy uniqueidentifier NULL,CONSTRAINT FK_InvRel_Partner FOREIGN KEY(PartnerId) REFERENCES dbo.Partners(Id),CONSTRAINT FK_InvRel_Source FOREIGN KEY(SourceInvoiceId) REFERENCES dbo.Invoices(Id),CONSTRAINT FK_InvRel_Target FOREIGN KEY(TargetInvoiceId) REFERENCES dbo.Invoices(Id),CONSTRAINT CK_InvRel_Type CHECK(RelationType IN('ADJUST','REPLACE','CANCEL')),CONSTRAINT CK_InvRel_Status CHECK(Status IN('PENDING','QUEUED','COMPLETED','FAILED','CANCELLED')));
 CREATE INDEX IX_InvRel_Source ON dbo.InvoiceRelations(SourceInvoiceId,RequestedAt DESC);
END
GO
IF COL_LENGTH('dbo.Invoices','LifecycleStatus') IS NULL ALTER TABLE dbo.Invoices ADD LifecycleStatus varchar(20) NOT NULL CONSTRAINT DF_Inv_Lifecycle DEFAULT 'ACTIVE';
IF COL_LENGTH('dbo.Invoices','UpdatedAt') IS NULL ALTER TABLE dbo.Invoices ADD UpdatedAt datetime2(3) NULL;
GO
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name='CK_Inv_Lifecycle') ALTER TABLE dbo.Invoices ADD CONSTRAINT CK_Inv_Lifecycle CHECK(LifecycleStatus IN('ACTIVE','ADJUSTED','REPLACED','CANCELLED'));
GO
IF OBJECT_ID(N'dbo.InvoiceProviderOperations',N'U') IS NULL BEGIN
 CREATE TABLE dbo.InvoiceProviderOperations(Id uniqueidentifier NOT NULL DEFAULT NEWSEQUENTIALID() CONSTRAINT PK_InvoiceProviderOperations PRIMARY KEY,PartnerId uniqueidentifier NOT NULL,InvoiceRequestId uniqueidentifier NULL,InvoiceId uniqueidentifier NULL,ProviderId uniqueidentifier NOT NULL,Operation varchar(40) NOT NULL,IdempotencyKey nvarchar(200) NOT NULL,Status varchar(20) NOT NULL DEFAULT 'PENDING',AttemptCount int NOT NULL DEFAULT 0,ExternalOperationId nvarchar(200) NULL,NextAttemptAt datetime2(3) NULL,LastError nvarchar(2000) NULL,CreatedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),CompletedAt datetime2(3) NULL,CONSTRAINT FK_IPO_Partner FOREIGN KEY(PartnerId) REFERENCES dbo.Partners(Id),CONSTRAINT FK_IPO_Request FOREIGN KEY(InvoiceRequestId) REFERENCES dbo.InvoiceRequests(Id),CONSTRAINT FK_IPO_Invoice FOREIGN KEY(InvoiceId) REFERENCES dbo.Invoices(Id),CONSTRAINT FK_IPO_Provider FOREIGN KEY(ProviderId) REFERENCES dbo.Providers(Id),CONSTRAINT CK_IPO_Operation CHECK(Operation IN('CREATE','ISSUE','ADJUST','REPLACE','CANCEL','GET_PDF','GET_XML','CHECK_STATE')),CONSTRAINT CK_IPO_Status CHECK(Status IN('PENDING','PROCESSING','SUCCEEDED','FAILED','DEAD')));
 CREATE UNIQUE INDEX UX_IPO_Idempotency ON dbo.InvoiceProviderOperations(PartnerId,ProviderId,IdempotencyKey);
 CREATE INDEX IX_IPO_WorkQueue ON dbo.InvoiceProviderOperations(Status,NextAttemptAt,CreatedAt) INCLUDE(Operation,AttemptCount);
END
GO
PRINT '027-invoice-lifecycle-hardening.sql: OK';
GO