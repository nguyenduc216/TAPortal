/* T.A Portal V1 RC5 fail-closed postflight. SQL Server 2014 compatible. */
USE [TAPortal];
GO
SET NOCOUNT ON; SET XACT_ABORT ON; SET QUOTED_IDENTIFIER ON;
GO
DECLARE @E TABLE(CheckName varchar(100),ProblemCount bigint);
INSERT @E SELECT 'FA_COMPANY_TENANT',COUNT(*) FROM dbo.FinancialAccounts a JOIN dbo.Companies c ON c.Id=a.CompanyId WHERE a.CompanyId IS NOT NULL AND a.PartnerId<>c.PartnerId;
INSERT @E SELECT 'PT_ACCOUNT_TENANT',COUNT(*) FROM dbo.PaymentTransactions t JOIN dbo.FinancialAccounts a ON a.Id=t.FinancialAccountId WHERE t.FinancialAccountId IS NOT NULL AND t.PartnerId<>a.PartnerId;
INSERT @E SELECT 'PI_ACCOUNT_TENANT',COUNT(*) FROM dbo.PaymentInstructions i JOIN dbo.FinancialAccounts a ON a.Id=i.FinancialAccountId WHERE i.FinancialAccountId IS NOT NULL AND i.PartnerId<>a.PartnerId;
INSERT @E SELECT 'CCA_CUSTOMER_TENANT',COUNT(*) FROM dbo.CustomerCashAccounts a JOIN dbo.Customers c ON c.Id=a.CustomerId WHERE a.PartnerId<>c.PartnerId;
INSERT @E SELECT 'REFUND_TENANT',COUNT(*) FROM dbo.RefundRequests r LEFT JOIN dbo.Customers c ON c.Id=r.CustomerId LEFT JOIN dbo.PaymentTransactions t ON t.Id=r.PaymentTransactionId LEFT JOIN dbo.CustomerCashAccounts a ON a.Id=r.CustomerCashAccountId WHERE (r.CustomerId IS NOT NULL AND c.PartnerId<>r.PartnerId) OR (r.PaymentTransactionId IS NOT NULL AND t.PartnerId<>r.PartnerId) OR (r.CustomerCashAccountId IS NOT NULL AND a.PartnerId<>r.PartnerId);
INSERT @E SELECT 'MATCH_TENANT',COUNT(*) FROM dbo.MatchingAttempts m JOIN dbo.PaymentTransactions t ON t.Id=m.PaymentTransactionId WHERE m.PartnerId<>t.PartnerId;
INSERT @E SELECT 'REVIEW_TENANT',COUNT(*) FROM dbo.PaymentReviewActions a JOIN dbo.PaymentTransactions t ON t.Id=a.PaymentTransactionId WHERE a.PartnerId<>t.PartnerId;
INSERT @E SELECT 'INVOICE_CUSTOMER_TENANT',COUNT(*) FROM dbo.InvoiceRequests i JOIN dbo.Customers c ON c.Id=i.CustomerId WHERE i.CustomerId IS NOT NULL AND i.PartnerId<>c.PartnerId;
INSERT @E SELECT 'GRANT_PERIOD_TENANT',COUNT(*) FROM dbo.SubscriptionCreditGrants g JOIN dbo.SubscriptionPeriods sp ON sp.Id=g.SubscriptionPeriodId JOIN dbo.PartnerSubscriptions s ON s.Id=sp.PartnerSubscriptionId WHERE g.PartnerId<>s.PartnerId;
INSERT @E SELECT 'CROSS_TENANT_ALLOCATION',COUNT(*) FROM dbo.PaymentAllocations a JOIN dbo.PaymentRequests r ON r.Id=a.PaymentRequestId JOIN dbo.PaymentTransactions t ON t.Id=a.PaymentTransactionId WHERE a.PartnerId<>r.PartnerId OR a.PartnerId<>t.PartnerId;
INSERT @E SELECT 'OVERALLOCATED_TRANSACTION',COUNT(*) FROM (SELECT a.PaymentTransactionId FROM dbo.PaymentAllocations a JOIN dbo.PaymentTransactions t ON t.Id=a.PaymentTransactionId WHERE a.Status='ACTIVE' GROUP BY a.PaymentTransactionId,t.Amount HAVING SUM(a.AllocatedAmount)>t.Amount)x;
INSERT @E SELECT 'OVERALLOCATED_RECEIVABLE',COUNT(*) FROM (SELECT a.PaymentRequestId FROM dbo.PaymentAllocations a JOIN dbo.PaymentRequests r ON r.Id=a.PaymentRequestId WHERE a.Status='ACTIVE' GROUP BY a.PaymentRequestId,r.AmountDue HAVING SUM(a.AllocatedAmount)>r.AmountDue)x;
INSERT @E SELECT 'CREDIT_GRANT_WITHOUT_LEDGER',COUNT(*) FROM dbo.SubscriptionCreditGrants WHERE Status='GRANTED' AND CreditLedgerId IS NULL;
INSERT @E SELECT 'ENTITLEMENT_USEDQUANTITY_NONZERO',COUNT(*) FROM dbo.SubscriptionEntitlements WHERE UsedQuantity<>0;
INSERT @E SELECT 'LEGACY_REQUEST_CODE_NEW_PATH',COUNT(*) FROM dbo.PaymentRequests WHERE PaymentCode IS NOT NULL AND CreatedAt >= '2026-10-01T00:00:00';
INSERT @E SELECT 'INVOICE_CONSUMED_RESERVATION_MISMATCH',COUNT(*) FROM dbo.InvoiceCreditConsumptions c JOIN dbo.CreditReservations r ON r.Id=c.CreditReservationId WHERE c.Status='CONSUMED' AND r.Status<>'CONSUMED';
DELETE FROM @E WHERE ProblemCount=0;
SELECT * FROM @E ORDER BY CheckName;
IF EXISTS(SELECT 1 FROM @E) THROW 51900,'V1 RC5 invariant postflight failed.',1;
IF OBJECT_ID('dbo.sp_FreezeInvoiceRequest','P') IS NULL THROW 51901,'Freeze procedure missing.',1;
IF OBJECT_ID('dbo.sp_PostCustomerCash','P') IS NULL OR NOT EXISTS(SELECT 1 FROM sys.parameters WHERE object_id=OBJECT_ID('dbo.sp_PostCustomerCash') AND name='@PartnerId') OR NOT EXISTS(SELECT 1 FROM sys.parameters WHERE object_id=OBJECT_ID('dbo.sp_PostCustomerCash') AND name='@IdempotencyKey') THROW 51902,'Customer cash canonical signature missing.',1;
IF OBJECT_ID('dbo.sp_GrantSubscriptionCredit','P') IS NULL OR NOT EXISTS(SELECT 1 FROM sys.parameters WHERE object_id=OBJECT_ID('dbo.sp_GrantSubscriptionCredit') AND name='@GrantId') THROW 51903,'Credit grant signature missing.',1;
IF OBJECT_ID('dbo.tr_PaymentRequests_LegacyInstructionReadOnly','TR') IS NULL OR OBJECT_ID('dbo.tr_SubscriptionEntitlements_UsedQuantityReadOnly','TR') IS NULL THROW 51904,'Legacy stop-write contract missing.',1;
IF OBJECT_ID('dbo.tr_FinancialAccounts_TenantOwnership','TR') IS NULL OR OBJECT_ID('dbo.tr_InvoiceRequests_TenantOwnership','TR') IS NULL OR OBJECT_ID('dbo.tr_SubscriptionCreditGrants_TenantOwnership','TR') IS NULL THROW 51905,'Tenant ownership guard missing.',1;
PRINT 'V1 RC5 POSTFLIGHT: PASS';
GO