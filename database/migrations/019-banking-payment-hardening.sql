/* TAPortal 019 - Banking identities, QR/payment instruction and reconciliation controls. */
USE [TAPortal];
GO
IF OBJECT_ID(N'dbo.CustomerBankIdentities',N'U') IS NULL BEGIN
 CREATE TABLE dbo.CustomerBankIdentities(
  Id uniqueidentifier NOT NULL CONSTRAINT PK_CustomerBankIdentities PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
  PartnerId uniqueidentifier NOT NULL,CustomerId uniqueidentifier NOT NULL,
  ProviderId uniqueidentifier NULL,BankCode varchar(30) NULL,AccountNumber nvarchar(150) NULL,AccountName nvarchar(250) NULL,
  IdentityKey nvarchar(300) NULL,IsVerified bit NOT NULL DEFAULT 0,IsActive bit NOT NULL DEFAULT 1,
  FirstSeenAt datetime2(3) NULL,LastSeenAt datetime2(3) NULL,CreatedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),UpdatedAt datetime2(3) NULL,
  CONSTRAINT FK_CBI_Partner FOREIGN KEY(PartnerId) REFERENCES dbo.Partners(Id),CONSTRAINT FK_CBI_Customer FOREIGN KEY(CustomerId) REFERENCES dbo.Customers(Id),CONSTRAINT FK_CBI_Provider FOREIGN KEY(ProviderId) REFERENCES dbo.Providers(Id)
 );
 CREATE INDEX IX_CBI_Identity ON dbo.CustomerBankIdentities(PartnerId,IdentityKey) WHERE IdentityKey IS NOT NULL AND IsActive=1;
END
GO
IF COL_LENGTH('dbo.PaymentRequests','BankAccountId') IS NULL ALTER TABLE dbo.PaymentRequests ADD BankAccountId uniqueidentifier NULL;
IF COL_LENGTH('dbo.PaymentRequests','CurrencyCode') IS NULL ALTER TABLE dbo.PaymentRequests ADD CurrencyCode char(3) NOT NULL CONSTRAINT DF_PR_Currency DEFAULT 'VND';
IF COL_LENGTH('dbo.PaymentRequests','PurposeType') IS NULL ALTER TABLE dbo.PaymentRequests ADD PurposeType varchar(50) NULL;
IF COL_LENGTH('dbo.PaymentRequests','PurposeId') IS NULL ALTER TABLE dbo.PaymentRequests ADD PurposeId nvarchar(200) NULL;
IF COL_LENGTH('dbo.PaymentRequests','QrProvider') IS NULL ALTER TABLE dbo.PaymentRequests ADD QrProvider varchar(50) NULL;
IF COL_LENGTH('dbo.PaymentRequests','QrPayload') IS NULL ALTER TABLE dbo.PaymentRequests ADD QrPayload nvarchar(2000) NULL;
IF COL_LENGTH('dbo.PaymentRequests','QrGeneratedAt') IS NULL ALTER TABLE dbo.PaymentRequests ADD QrGeneratedAt datetime2(3) NULL;
GO
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name='FK_PR_BankAccount') ALTER TABLE dbo.PaymentRequests ADD CONSTRAINT FK_PR_BankAccount FOREIGN KEY(BankAccountId) REFERENCES dbo.PartnerBankAccounts(Id);
GO
IF OBJECT_ID(N'dbo.PaymentRequestEvents',N'U') IS NULL BEGIN
 CREATE TABLE dbo.PaymentRequestEvents(
  Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_PaymentRequestEvents PRIMARY KEY,
  PaymentRequestId uniqueidentifier NOT NULL,EventType varchar(40) NOT NULL,OldStatus varchar(30) NULL,NewStatus varchar(30) NULL,
  BankTransactionId uniqueidentifier NULL,PaymentAllocationId uniqueidentifier NULL,Amount decimal(18,2) NULL,
  Note nvarchar(1000) NULL,ActorType varchar(20) NOT NULL DEFAULT 'SYSTEM',ActorId uniqueidentifier NULL,
  CreatedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
  CONSTRAINT FK_PRE_Request FOREIGN KEY(PaymentRequestId) REFERENCES dbo.PaymentRequests(Id),
  CONSTRAINT FK_PRE_Transaction FOREIGN KEY(BankTransactionId) REFERENCES dbo.BankTransactions(Id),
  CONSTRAINT FK_PRE_Allocation FOREIGN KEY(PaymentAllocationId) REFERENCES dbo.PaymentAllocations(Id),
  CONSTRAINT CK_PRE_Actor CHECK(ActorType IN('SYSTEM','USER','PROVIDER','WORKER'))
 );
 CREATE INDEX IX_PRE_RequestCreated ON dbo.PaymentRequestEvents(PaymentRequestId,CreatedAt DESC);
END
GO
IF OBJECT_ID(N'dbo.BankReconciliationRuns',N'U') IS NULL BEGIN
 CREATE TABLE dbo.BankReconciliationRuns(
  Id uniqueidentifier NOT NULL CONSTRAINT PK_BankReconciliationRuns PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
  PartnerProviderConnectionId uniqueidentifier NOT NULL,BankAccountId uniqueidentifier NULL,
  WindowFrom datetime2(3) NOT NULL,WindowTo datetime2(3) NOT NULL,Status varchar(20) NOT NULL DEFAULT 'PENDING',
  ProviderTransactions int NOT NULL DEFAULT 0,InsertedTransactions int NOT NULL DEFAULT 0,ExistingTransactions int NOT NULL DEFAULT 0,
  FailedTransactions int NOT NULL DEFAULT 0,StartedAt datetime2(3) NULL,CompletedAt datetime2(3) NULL,
  Cursor nvarchar(500) NULL,ErrorMessage nvarchar(2000) NULL,CreatedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
  CONSTRAINT FK_BRR_Connection FOREIGN KEY(PartnerProviderConnectionId) REFERENCES dbo.PartnerProviderConnections(Id),
  CONSTRAINT FK_BRR_Account FOREIGN KEY(BankAccountId) REFERENCES dbo.PartnerBankAccounts(Id),
  CONSTRAINT CK_BRR_Status CHECK(Status IN('PENDING','RUNNING','COMPLETED','PARTIAL','FAILED'))
 );
 CREATE INDEX IX_BRR_ConnectionWindow ON dbo.BankReconciliationRuns(PartnerProviderConnectionId,WindowFrom DESC);
END
GO
IF OBJECT_ID(N'dbo.BankTransactionReviewQueue',N'U') IS NULL BEGIN
 CREATE TABLE dbo.BankTransactionReviewQueue(
  Id uniqueidentifier NOT NULL CONSTRAINT PK_BankTransactionReviewQueue PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
  BankTransactionId uniqueidentifier NOT NULL,ReasonCode varchar(50) NOT NULL,Status varchar(20) NOT NULL DEFAULT 'OPEN',
  SuggestedCustomerId uniqueidentifier NULL,SuggestedPaymentRequestId uniqueidentifier NULL,Note nvarchar(1000) NULL,
  CreatedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),ResolvedAt datetime2(3) NULL,ResolvedBy uniqueidentifier NULL,
  CONSTRAINT FK_BTRQ_Transaction FOREIGN KEY(BankTransactionId) REFERENCES dbo.BankTransactions(Id),
  CONSTRAINT FK_BTRQ_Customer FOREIGN KEY(SuggestedCustomerId) REFERENCES dbo.Customers(Id),
  CONSTRAINT FK_BTRQ_Request FOREIGN KEY(SuggestedPaymentRequestId) REFERENCES dbo.PaymentRequests(Id),
  CONSTRAINT CK_BTRQ_Status CHECK(Status IN('OPEN','RESOLVED','IGNORED'))
 );
 CREATE UNIQUE INDEX UX_BTRQ_OpenReason ON dbo.BankTransactionReviewQueue(BankTransactionId,ReasonCode) WHERE Status='OPEN';
END
GO
CREATE OR ALTER VIEW dbo.vw_BankTransactionAllocationSummary AS
SELECT t.Id,t.PartnerId,t.BankAccountId,t.ExternalTransactionId,t.TransactionDate,t.Direction,t.Amount,t.PaymentCode,t.Content,
 CAST(ISNULL(SUM(CASE WHEN a.Status='ACTIVE' THEN a.AllocatedAmount ELSE 0 END),0) AS decimal(18,2)) AllocatedAmount,
 CAST(t.Amount-ISNULL(SUM(CASE WHEN a.Status='ACTIVE' THEN a.AllocatedAmount ELSE 0 END),0) AS decimal(18,2)) UnallocatedAmount,
 CASE WHEN ISNULL(SUM(CASE WHEN a.Status='ACTIVE' THEN a.AllocatedAmount ELSE 0 END),0)=0 THEN 'UNALLOCATED'
      WHEN ISNULL(SUM(CASE WHEN a.Status='ACTIVE' THEN a.AllocatedAmount ELSE 0 END),0)<t.Amount THEN 'PARTIALLY_ALLOCATED'
      WHEN ISNULL(SUM(CASE WHEN a.Status='ACTIVE' THEN a.AllocatedAmount ELSE 0 END),0)=t.Amount THEN 'FULLY_ALLOCATED'
      ELSE 'OVER_ALLOCATED' END AllocationStatus
FROM dbo.BankTransactions t LEFT JOIN dbo.PaymentAllocations a ON a.BankTransactionId=t.Id
GROUP BY t.Id,t.PartnerId,t.BankAccountId,t.ExternalTransactionId,t.TransactionDate,t.Direction,t.Amount,t.PaymentCode,t.Content;
GO
PRINT '019-banking-payment-hardening.sql: OK';
GO