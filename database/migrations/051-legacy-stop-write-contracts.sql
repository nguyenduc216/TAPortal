/* V1 RC5 051 - stop independent writes to deprecated V1 sources of truth. */
USE [TAPortal];
GO
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO
/* PaymentInstruction/Registry is canonical. Legacy request code remains historical compatibility only. */
IF COL_LENGTH('dbo.PaymentRequests','PaymentCode') IS NOT NULL
BEGIN
 ALTER TABLE dbo.PaymentRequests ALTER COLUMN PaymentCode varchar(100) NULL;
END
GO
IF OBJECT_ID(N'dbo.tr_PaymentRequests_LegacyInstructionReadOnly',N'TR') IS NULL
EXEC('CREATE TRIGGER dbo.tr_PaymentRequests_LegacyInstructionReadOnly ON dbo.PaymentRequests AFTER UPDATE AS BEGIN SET NOCOUNT ON; IF UPDATE(PaymentCode) AND EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id WHERE ISNULL(i.PaymentCode,'''')<>ISNULL(d.PaymentCode,'''')) THROW 51830,''PaymentRequests.PaymentCode is deprecated/read-only; use PaymentInstructions/PaymentCodeRegistry.'',1; END');
GO
/* Consumable entitlement usage is derived from wallet/ledger, never a writable quota counter. */
IF COL_LENGTH('dbo.SubscriptionEntitlements','UsedQuantity') IS NOT NULL AND OBJECT_ID(N'dbo.tr_SubscriptionEntitlements_UsedQuantityReadOnly',N'TR') IS NULL
EXEC('CREATE TRIGGER dbo.tr_SubscriptionEntitlements_UsedQuantityReadOnly ON dbo.SubscriptionEntitlements AFTER UPDATE AS BEGIN SET NOCOUNT ON; IF UPDATE(UsedQuantity) AND EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id WHERE ISNULL(i.UsedQuantity,0)<>ISNULL(d.UsedQuantity,0)) THROW 51831,''UsedQuantity is deprecated for consumable quota; wallet/ledger is canonical.'',1; END');
GO
/* Legacy CreditType must agree with canonical CreditDefinitionId; it cannot independently select another balance. */
IF OBJECT_ID(N'dbo.tr_Wallet_CreditDefinitionConsistency',N'TR') IS NULL
EXEC('CREATE TRIGGER dbo.tr_Wallet_CreditDefinitionConsistency ON dbo.PartnerCreditWallets AFTER INSERT,UPDATE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.CreditDefinitions d ON d.Id=i.CreditDefinitionId WHERE i.CreditType<>CASE d.Code WHEN ''BANK_TRANSACTION_UNIT'' THEN ''BANKING'' WHEN ''INVOICE_ISSUE_UNIT'' THEN ''EINVOICE'' ELSE i.CreditType END) THROW 51832,''Legacy CreditType conflicts with CreditDefinitionId.'',1; END');
GO
IF OBJECT_ID(N'dbo.tr_Reservation_CreditDefinitionConsistency',N'TR') IS NULL
EXEC('CREATE TRIGGER dbo.tr_Reservation_CreditDefinitionConsistency ON dbo.CreditReservations AFTER INSERT,UPDATE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.CreditDefinitions d ON d.Id=i.CreditDefinitionId WHERE i.CreditType<>CASE d.Code WHEN ''BANK_TRANSACTION_UNIT'' THEN ''BANKING'' WHEN ''INVOICE_ISSUE_UNIT'' THEN ''EINVOICE'' ELSE i.CreditType END) THROW 51833,''Legacy CreditType conflicts with CreditDefinitionId.'',1; END');
GO
/* Old reconciliation tables are preserved as history but cannot receive new canonical work. */
IF OBJECT_ID(N'dbo.BankSyncJobs',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.tr_BankSyncJobs_DeprecatedReadOnly',N'TR') IS NULL EXEC('CREATE TRIGGER dbo.tr_BankSyncJobs_DeprecatedReadOnly ON dbo.BankSyncJobs AFTER INSERT,UPDATE AS BEGIN SET NOCOUNT ON; THROW 51834,''BankSyncJobs is deprecated/read-only; use ReconciliationRuns.'',1; END');
GO
IF OBJECT_ID(N'dbo.BankReconciliationRuns',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.tr_BankReconciliationRuns_DeprecatedReadOnly',N'TR') IS NULL EXEC('CREATE TRIGGER dbo.tr_BankReconciliationRuns_DeprecatedReadOnly ON dbo.BankReconciliationRuns AFTER INSERT,UPDATE AS BEGIN SET NOCOUNT ON; THROW 51835,''BankReconciliationRuns is deprecated/read-only; use ReconciliationRuns.'',1; END');
GO
PRINT '051-legacy-stop-write-contracts.sql: OK';
GO