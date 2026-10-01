/* T.A Portal V1 RC3 fail-closed postflight. SQL Server 2014 compatible. */
USE [TAPortal];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO
DECLARE @Errors TABLE(CheckName varchar(100),ProblemCount bigint,Details nvarchar(1000));
INSERT @Errors SELECT 'COMPANY_PARTNER_ORPHAN',COUNT(*),N'Company PartnerId must resolve' FROM dbo.Companies c LEFT JOIN dbo.Partners p ON p.Id=c.PartnerId WHERE c.PartnerId IS NULL OR p.Id IS NULL;
INSERT @Errors SELECT 'CUSTOMER_PARTNER_ORPHAN',COUNT(*),N'Customer PartnerId must resolve' FROM dbo.Customers c LEFT JOIN dbo.Partners p ON p.Id=c.PartnerId WHERE c.PartnerId IS NULL OR p.Id IS NULL;
INSERT @Errors SELECT 'WALLET_INVARIANT',COUNT(*),N'Balance/reserved invariant' FROM dbo.PartnerCreditWallets WHERE CurrentBalance<0 OR ReservedBalance<0 OR ReservedBalance>CurrentBalance;
INSERT @Errors SELECT 'CROSS_TENANT_ALLOCATION',COUNT(*),N'Allocation tenant mismatch' FROM dbo.PaymentAllocations a JOIN dbo.PaymentRequests r ON r.Id=a.PaymentRequestId JOIN dbo.PaymentTransactions t ON t.Id=a.PaymentTransactionId WHERE a.PartnerId<>r.PartnerId OR a.PartnerId<>t.PartnerId;
INSERT @Errors SELECT 'OVERALLOCATED_TRANSACTION',COUNT(*),N'Active allocations exceed transaction' FROM (SELECT a.PaymentTransactionId FROM dbo.PaymentAllocations a JOIN dbo.PaymentTransactions t ON t.Id=a.PaymentTransactionId WHERE a.Status='ACTIVE' GROUP BY a.PaymentTransactionId,t.Amount HAVING SUM(a.AllocatedAmount)>t.Amount)x;
INSERT @Errors SELECT 'OVERALLOCATED_RECEIVABLE',COUNT(*),N'Active allocations exceed receivable' FROM (SELECT a.PaymentRequestId FROM dbo.PaymentAllocations a JOIN dbo.PaymentRequests r ON r.Id=a.PaymentRequestId WHERE a.Status='ACTIVE' GROUP BY a.PaymentRequestId,r.AmountDue HAVING SUM(a.AllocatedAmount)>r.AmountDue)x;
INSERT @Errors SELECT 'AMOUNT_ONLY_AUTO_MATCH',COUNT(*),N'Amount-only matching cannot auto allocate' FROM dbo.PaymentMatchingRules WHERE RuleType='AMOUNT_ASSIST' AND AutoAllocate=1;
INSERT @Errors SELECT 'PLAINTEXT_HOSTED_LINK',COUNT(*),N'Hosted link must not persist' FROM dbo.BankLinkSessions WHERE HostedLinkUrl IS NOT NULL;
INSERT @Errors SELECT 'CROSS_PARTNER_ROLE',COUNT(*),N'Partner role must match membership' FROM dbo.PartnerUserRoles WHERE RolePartnerId<>PartnerId;
INSERT @Errors SELECT 'CREDIT_GRANT_WITHOUT_LEDGER',COUNT(*),N'Granted subscription credit must link ledger' FROM dbo.SubscriptionCreditGrants WHERE Status='GRANTED' AND CreditLedgerId IS NULL;
INSERT @Errors SELECT 'FROZEN_INVOICE_TOTAL_MISMATCH',COUNT(*),N'Frozen invoice totals must reconcile' FROM dbo.InvoiceRequests WHERE SnapshotFrozenAt IS NOT NULL AND ABS((ISNULL(SubtotalAmount,0)-ISNULL(DiscountAmount,0)+ISNULL(VatAmount,0)+ISNULL(OtherChargesAmount,0))-ISNULL(GrandTotalAmount,0))>0.01;
DELETE FROM @Errors WHERE ProblemCount=0;
SELECT * FROM @Errors ORDER BY CheckName;
IF EXISTS(SELECT 1 FROM @Errors) THROW 51500,'V1 RC3 postflight failed. Review result set.',1;
DECLARE @Required TABLE(ObjectName sysname,ObjectType char(2));
INSERT @Required VALUES('Partners','U'),('PartnerRoles','U'),('FinancialAccounts','U'),('PaymentTransactions','U'),('PaymentInstructions','U'),('PaymentCodeRegistry','U'),('CustomerCashAccounts','U'),('CustomerCashLedger','U'),('RefundRequests','U'),('MatchingAttempts','U'),('CreditDefinitions','U'),('SubscriptionCreditGrants','U'),('InvoiceCreditConsumptions','U'),('InboxMessages','U'),('SchemaMigrationHistory','U'),('sp_AllocatePaymentTransaction','P'),('sp_PostCustomerCash','P'),('sp_GrantSubscriptionCredit','P'),('sp_FreezeInvoiceRequest','P');
IF EXISTS(SELECT 1 FROM @Required WHERE OBJECT_ID('dbo.'+ObjectName,ObjectType) IS NULL) BEGIN SELECT * FROM @Required WHERE OBJECT_ID('dbo.'+ObjectName,ObjectType) IS NULL; THROW 51501,'Required V1 RC3 object missing.',1; END;
PRINT 'V1 RC3 POSTFLIGHT: PASS';
GO