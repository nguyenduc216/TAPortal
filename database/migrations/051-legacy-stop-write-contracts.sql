/* V1 RC5 051 - stop independent writes to deprecated V1 sources of truth. */
USE [TAPortal];
GO
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO
/* PaymentInstruction/Registry is canonical; request-level code fields are historical compatibility only. */
IF EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.PaymentRequests') AND name='UX_PR_PartnerPaymentCode') DROP INDEX UX_PR_PartnerPaymentCode ON dbo.PaymentRequests;
IF EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.PaymentRequests') AND name='UX_PR_NormalizedPaymentCode') DROP INDEX UX_PR_NormalizedPaymentCode ON dbo.PaymentRequests;
ALTER TABLE dbo.PaymentRequests ALTER COLUMN PaymentCode varchar(100) NULL;
ALTER TABLE dbo.PaymentRequests ALTER COLUMN PaymentCodeNormalized varchar(100) NULL;
GO
IF OBJECT_ID(N'dbo.tr_PaymentRequests_LegacyInstructionReadOnly',N'TR') IS NULL EXEC('CREATE TRIGGER dbo.tr_PaymentRequests_LegacyInstructionReadOnly ON dbo.PaymentRequests AFTER INSERT,UPDATE AS BEGIN SET NOCOUNT ON; END');
GO
ALTER TRIGGER dbo.tr_PaymentRequests_LegacyInstructionReadOnly ON dbo.PaymentRequests AFTER INSERT,UPDATE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(
   SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.Id=i.Id
   WHERE (d.Id IS NULL AND (i.PaymentCode IS NOT NULL OR i.PaymentCodeNormalized IS NOT NULL))
      OR (d.Id IS NOT NULL AND (ISNULL(i.PaymentCode,'')<>ISNULL(d.PaymentCode,'') OR ISNULL(i.PaymentCodeNormalized,'')<>ISNULL(d.PaymentCodeNormalized,'')))
 ) THROW 51830,'Legacy request payment-code fields are read-only; use PaymentInstructions/PaymentCodeRegistry.',1;
END
GO
IF OBJECT_ID(N'dbo.PaymentCodeAliases',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.tr_PaymentCodeAliases_DeprecatedReadOnly',N'TR') IS NULL EXEC('CREATE TRIGGER dbo.tr_PaymentCodeAliases_DeprecatedReadOnly ON dbo.PaymentCodeAliases AFTER INSERT,UPDATE,DELETE AS BEGIN SET NOCOUNT ON; THROW 51836,''PaymentCodeAliases is deprecated/read-only; use PaymentCodeRegistry.'',1; END');
GO
/* Consumable usage is wallet/ledger-derived, never a second mutable counter. */
IF OBJECT_ID(N'dbo.tr_SubscriptionEntitlements_UsedQuantityReadOnly',N'TR') IS NULL EXEC('CREATE TRIGGER dbo.tr_SubscriptionEntitlements_UsedQuantityReadOnly ON dbo.SubscriptionEntitlements AFTER UPDATE AS BEGIN SET NOCOUNT ON; END');
GO
ALTER TRIGGER dbo.tr_SubscriptionEntitlements_UsedQuantityReadOnly ON dbo.SubscriptionEntitlements AFTER UPDATE AS
BEGIN
 SET NOCOUNT ON;
 IF UPDATE(UsedQuantity) AND EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id WHERE ISNULL(i.UsedQuantity,0)<>ISNULL(d.UsedQuantity,0))
  THROW 51831,'UsedQuantity is deprecated for consumable quota; wallet/ledger is canonical.',1;
END
GO
ALTER VIEW dbo.vw_PartnerServiceEntitlements AS
SELECT s.PartnerId,sv.Code ServiceCode,sv.Name ServiceName,
       SUM(CASE WHEN e.Status='ACTIVE' THEN ISNULL(e.GrantedQuantity,0) ELSE 0 END) GrantedQuantity,
       CAST(NULL AS decimal(18,2)) UsedQuantity
FROM dbo.PartnerSubscriptions s
JOIN dbo.SubscriptionEntitlements e ON e.PartnerSubscriptionId=s.Id
JOIN dbo.Services sv ON sv.Id=e.ServiceId
WHERE s.Status='ACTIVE'
GROUP BY s.PartnerId,sv.Code,sv.Name;
GO
/* Legacy CreditType is compatibility metadata only and must agree with canonical CreditDefinitionId. */
IF OBJECT_ID(N'dbo.tr_Wallet_CreditDefinitionConsistency',N'TR') IS NULL EXEC('CREATE TRIGGER dbo.tr_Wallet_CreditDefinitionConsistency ON dbo.PartnerCreditWallets AFTER INSERT,UPDATE AS BEGIN SET NOCOUNT ON; END');
GO
ALTER TRIGGER dbo.tr_Wallet_CreditDefinitionConsistency ON dbo.PartnerCreditWallets AFTER INSERT,UPDATE AS
BEGIN SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.CreditDefinitions d ON d.Id=i.CreditDefinitionId WHERE i.CreditType<>CASE d.Code WHEN 'BANK_TRANSACTION_UNIT' THEN 'BANKING' WHEN 'INVOICE_ISSUE_UNIT' THEN 'EINVOICE' ELSE i.CreditType END) THROW 51832,'Legacy CreditType conflicts with CreditDefinitionId.',1;
END
GO
IF OBJECT_ID(N'dbo.tr_Reservation_CreditDefinitionConsistency',N'TR') IS NULL EXEC('CREATE TRIGGER dbo.tr_Reservation_CreditDefinitionConsistency ON dbo.CreditReservations AFTER INSERT,UPDATE AS BEGIN SET NOCOUNT ON; END');
GO
ALTER TRIGGER dbo.tr_Reservation_CreditDefinitionConsistency ON dbo.CreditReservations AFTER INSERT,UPDATE AS
BEGIN SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.CreditDefinitions d ON d.Id=i.CreditDefinitionId WHERE i.CreditType<>CASE d.Code WHEN 'BANK_TRANSACTION_UNIT' THEN 'BANKING' WHEN 'INVOICE_ISSUE_UNIT' THEN 'EINVOICE' ELSE i.CreditType END) THROW 51833,'Legacy CreditType conflicts with CreditDefinitionId.',1;
END
GO
IF OBJECT_ID(N'dbo.tr_Topups_CreditDefinitionConsistency',N'TR') IS NULL EXEC('CREATE TRIGGER dbo.tr_Topups_CreditDefinitionConsistency ON dbo.CreditTopups AFTER INSERT,UPDATE AS BEGIN SET NOCOUNT ON; END');
GO
ALTER TRIGGER dbo.tr_Topups_CreditDefinitionConsistency ON dbo.CreditTopups AFTER INSERT,UPDATE AS
BEGIN SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.CreditDefinitions d ON d.Id=i.CreditDefinitionId WHERE i.CreditDefinitionId IS NOT NULL AND i.CreditType<>CASE d.Code WHEN 'BANK_TRANSACTION_UNIT' THEN 'BANKING' WHEN 'INVOICE_ISSUE_UNIT' THEN 'EINVOICE' ELSE i.CreditType END) THROW 51837,'Legacy CreditType conflicts with CreditDefinitionId.',1;
END
GO
/* Old reconciliation tables preserve history but cannot receive new canonical work. */
IF OBJECT_ID(N'dbo.BankSyncJobs',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.tr_BankSyncJobs_DeprecatedReadOnly',N'TR') IS NULL EXEC('CREATE TRIGGER dbo.tr_BankSyncJobs_DeprecatedReadOnly ON dbo.BankSyncJobs AFTER INSERT,UPDATE,DELETE AS BEGIN SET NOCOUNT ON; THROW 51834,''BankSyncJobs is deprecated/read-only; use ReconciliationRuns.'',1; END');
GO
IF OBJECT_ID(N'dbo.BankReconciliationRuns',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.tr_BankReconciliationRuns_DeprecatedReadOnly',N'TR') IS NULL EXEC('CREATE TRIGGER dbo.tr_BankReconciliationRuns_DeprecatedReadOnly ON dbo.BankReconciliationRuns AFTER INSERT,UPDATE,DELETE AS BEGIN SET NOCOUNT ON; THROW 51835,''BankReconciliationRuns is deprecated/read-only; use ReconciliationRuns.'',1; END');
GO
PRINT '051-legacy-stop-write-contracts.sql: OK';
GO