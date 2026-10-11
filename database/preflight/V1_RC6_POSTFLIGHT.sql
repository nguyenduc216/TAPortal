/* T.A Portal V1 RC6 final-freeze postflight. SQL Server 2014 compatible. */
USE [TAPortal];
GO
SET NOCOUNT ON; SET XACT_ABORT ON; SET QUOTED_IDENTIFIER ON;
GO
DECLARE @E TABLE(CheckName varchar(100),ProblemCount bigint);
INSERT @E SELECT 'WALLET_NULL_DEFINITION',COUNT(*) FROM dbo.PartnerCreditWallets WHERE CreditDefinitionId IS NULL;
INSERT @E SELECT 'RESERVATION_NULL_DEFINITION',COUNT(*) FROM dbo.CreditReservations WHERE CreditDefinitionId IS NULL;
INSERT @E SELECT 'TOPUP_NULL_DEFINITION',COUNT(*) FROM dbo.CreditTopups WHERE CreditDefinitionId IS NULL;
INSERT @E SELECT 'LEDGER_NULL_DEFINITION',COUNT(*) FROM dbo.CreditLedger WHERE CreditDefinitionId IS NULL;
INSERT @E SELECT 'RULE_NULL_DEFINITION',COUNT(*) FROM dbo.CreditUsageRules WHERE CreditDefinitionId IS NULL;
INSERT @E SELECT 'WALLET_TYPE_MISMATCH',COUNT(*) FROM dbo.PartnerCreditWallets w JOIN dbo.CreditDefinitions d ON d.Id=w.CreditDefinitionId WHERE w.CreditType<>CASE d.Code WHEN 'BANK_TRANSACTION_UNIT' THEN 'BANKING' WHEN 'INVOICE_ISSUE_UNIT' THEN 'EINVOICE' ELSE w.CreditType END;
INSERT @E SELECT 'RESERVATION_TYPE_MISMATCH',COUNT(*) FROM dbo.CreditReservations r JOIN dbo.CreditDefinitions d ON d.Id=r.CreditDefinitionId WHERE r.CreditType<>CASE d.Code WHEN 'BANK_TRANSACTION_UNIT' THEN 'BANKING' WHEN 'INVOICE_ISSUE_UNIT' THEN 'EINVOICE' ELSE r.CreditType END;
INSERT @E SELECT 'TOPUP_TYPE_MISMATCH',COUNT(*) FROM dbo.CreditTopups t JOIN dbo.CreditDefinitions d ON d.Id=t.CreditDefinitionId WHERE t.CreditType<>CASE d.Code WHEN 'BANK_TRANSACTION_UNIT' THEN 'BANKING' WHEN 'INVOICE_ISSUE_UNIT' THEN 'EINVOICE' ELSE t.CreditType END;
INSERT @E SELECT 'GRANTED_WITHOUT_LEDGER',COUNT(*) FROM dbo.SubscriptionCreditGrants WHERE Status='GRANTED' AND CreditLedgerId IS NULL;
INSERT @E SELECT 'ENTITLEMENT_USED_NONZERO',COUNT(*) FROM dbo.SubscriptionEntitlements WHERE UsedQuantity<>0;
INSERT @E SELECT 'CROSS_TENANT_ALLOCATION',COUNT(*) FROM dbo.PaymentAllocations a JOIN dbo.PaymentRequests r ON r.Id=a.PaymentRequestId JOIN dbo.PaymentTransactions t ON t.Id=a.PaymentTransactionId WHERE a.PartnerId<>r.PartnerId OR a.PartnerId<>t.PartnerId;
INSERT @E SELECT 'OVERALLOCATED_TRANSACTION',COUNT(*) FROM (SELECT a.PaymentTransactionId FROM dbo.PaymentAllocations a JOIN dbo.PaymentTransactions t ON t.Id=a.PaymentTransactionId WHERE a.Status='ACTIVE' GROUP BY a.PaymentTransactionId,t.Amount HAVING SUM(a.AllocatedAmount)>t.Amount)x;
INSERT @E SELECT 'OVERALLOCATED_RECEIVABLE',COUNT(*) FROM (SELECT a.PaymentRequestId FROM dbo.PaymentAllocations a JOIN dbo.PaymentRequests r ON r.Id=a.PaymentRequestId WHERE a.Status='ACTIVE' GROUP BY a.PaymentRequestId,r.AmountDue HAVING SUM(a.AllocatedAmount)>r.AmountDue)x;
DELETE FROM @E WHERE ProblemCount=0;
SELECT * FROM @E ORDER BY CheckName;
IF EXISTS(SELECT 1 FROM @E) THROW 52100,'V1 RC6 invariant postflight failed.',1;
IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('dbo.PartnerCreditWallets') AND name='CreditDefinitionId' AND is_nullable=1) THROW 52101,'Wallet CreditDefinitionId must be NOT NULL.',1;
IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('dbo.CreditReservations') AND name='CreditDefinitionId' AND is_nullable=1) THROW 52102,'Reservation CreditDefinitionId must be NOT NULL.',1;
IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('dbo.CreditTopups') AND name='CreditDefinitionId' AND is_nullable=1) THROW 52103,'Topup CreditDefinitionId must be NOT NULL.',1;
IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('dbo.CreditLedger') AND name='CreditDefinitionId' AND is_nullable=1) THROW 52104,'Ledger CreditDefinitionId must be NOT NULL.',1;
IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('dbo.CreditUsageRules') AND name='CreditDefinitionId' AND is_nullable=1) THROW 52105,'Usage-rule CreditDefinitionId must be NOT NULL.',1;
IF OBJECT_ID('dbo.sp_ReserveCreditDefinition','P') IS NULL THROW 52106,'Canonical reservation procedure missing.',1;
IF OBJECT_ID('dbo.tr_PaymentRequests_LegacyQrReadOnly','TR') IS NULL THROW 52107,'Legacy QR stop-write trigger missing.',1;
IF OBJECT_ID('dbo.tr_PartnerUsers_LegacyRoleReadOnly','TR') IS NULL THROW 52108,'Legacy PartnerRole stop-write trigger missing.',1;
PRINT 'V1 RC6 POSTFLIGHT: PASS';
GO