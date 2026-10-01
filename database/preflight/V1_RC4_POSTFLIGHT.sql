/* T.A Portal V1 RC4 fail-closed postflight. Run only after successful 001-048 chain. */
USE [TAPortal];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO
DECLARE @E TABLE(CheckName varchar(100),ProblemCount bigint);
INSERT @E SELECT 'COMPANY_PARTNER_ORPHAN',COUNT(*) FROM dbo.Companies c LEFT JOIN dbo.Partners p ON p.Id=c.PartnerId WHERE c.PartnerId IS NULL OR p.Id IS NULL;
INSERT @E SELECT 'CUSTOMER_PARTNER_ORPHAN',COUNT(*) FROM dbo.Customers c LEFT JOIN dbo.Partners p ON p.Id=c.PartnerId WHERE c.PartnerId IS NULL OR p.Id IS NULL;
INSERT @E SELECT 'WALLET_INVARIANT',COUNT(*) FROM dbo.PartnerCreditWallets WHERE CurrentBalance<0 OR ReservedBalance<0 OR ReservedBalance>CurrentBalance;
INSERT @E SELECT 'CROSS_TENANT_ALLOCATION',COUNT(*) FROM dbo.PaymentAllocations a JOIN dbo.PaymentRequests r ON r.Id=a.PaymentRequestId JOIN dbo.PaymentTransactions t ON t.Id=a.PaymentTransactionId WHERE a.PartnerId<>r.PartnerId OR a.PartnerId<>t.PartnerId;
INSERT @E SELECT 'OVERALLOCATED_TRANSACTION',COUNT(*) FROM (SELECT a.PaymentTransactionId FROM dbo.PaymentAllocations a JOIN dbo.PaymentTransactions t ON t.Id=a.PaymentTransactionId WHERE a.Status='ACTIVE' GROUP BY a.PaymentTransactionId,t.Amount HAVING SUM(a.AllocatedAmount)>t.Amount)x;
INSERT @E SELECT 'OVERALLOCATED_RECEIVABLE',COUNT(*) FROM (SELECT a.PaymentRequestId FROM dbo.PaymentAllocations a JOIN dbo.PaymentRequests r ON r.Id=a.PaymentRequestId WHERE a.Status='ACTIVE' GROUP BY a.PaymentRequestId,r.AmountDue HAVING SUM(a.AllocatedAmount)>r.AmountDue)x;
INSERT @E SELECT 'AMOUNT_ONLY_AUTO_MATCH',COUNT(*) FROM dbo.PaymentMatchingRules WHERE RuleType='AMOUNT_ASSIST' AND AutoAllocate=1;
INSERT @E SELECT 'PLAINTEXT_HOSTED_LINK',COUNT(*) FROM dbo.BankLinkSessions WHERE HostedLinkUrl IS NOT NULL;
INSERT @E SELECT 'CREDIT_GRANT_WITHOUT_LEDGER',COUNT(*) FROM dbo.SubscriptionCreditGrants WHERE Status='GRANTED' AND CreditLedgerId IS NULL;
INSERT @E SELECT 'INVOICE_CONSUMED_WITH_ACTIVE_RESERVATION',COUNT(*) FROM dbo.InvoiceCreditConsumptions c JOIN dbo.CreditReservations r ON r.Id=c.CreditReservationId WHERE c.Status='CONSUMED' AND r.Status<>'CONSUMED';
DELETE FROM @E WHERE ProblemCount=0;
SELECT * FROM @E ORDER BY CheckName;
IF EXISTS(SELECT 1 FROM @E) THROW 51700,'V1 RC4 invariant postflight failed.',1;
IF EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.PaymentAllocations') AND name='UX_PA_RequestTransactionActive') THROW 51701,'Legacy allocation unique index still exists.',1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.PaymentAllocations') AND name='UX_PA_RequestPaymentTransactionActive') THROW 51702,'Generic allocation unique index missing.',1;
IF OBJECT_ID('dbo.tr_InvoiceItems_FrozenSnapshot','TR') IS NULL THROW 51703,'Invoice item freeze trigger missing.',1;
IF OBJECT_ID('dbo.sp_GrantSubscriptionCredit','P') IS NULL OR NOT EXISTS(SELECT 1 FROM sys.parameters WHERE object_id=OBJECT_ID('dbo.sp_GrantSubscriptionCredit') AND name='@GrantId') THROW 51704,'Subscription grant procedure/signature missing.',1;
IF OBJECT_ID('dbo.sp_AllocatePaymentTransaction','P') IS NULL OR NOT EXISTS(SELECT 1 FROM sys.parameters WHERE object_id=OBJECT_ID('dbo.sp_AllocatePaymentTransaction') AND name='@OperationKey') THROW 51705,'Generic allocation procedure/signature missing.',1;
IF OBJECT_ID('dbo.sp_PostCustomerCash','P') IS NULL OR NOT EXISTS(SELECT 1 FROM sys.parameters WHERE object_id=OBJECT_ID('dbo.sp_PostCustomerCash') AND name='@IdempotencyKey') THROW 51706,'Customer cash procedure/signature missing.',1;
IF OBJECT_ID('dbo.sp_ClaimOutboxMessage','P') IS NULL OR OBJECT_ID('dbo.sp_CompleteOutboxMessage','P') IS NULL OR OBJECT_ID('dbo.sp_FailOutboxMessage','P') IS NULL THROW 51707,'Outbox worker contract incomplete.',1;
IF OBJECT_ID('dbo.sp_CompleteInboxMessage','P') IS NULL OR OBJECT_ID('dbo.sp_FailInboxMessage','P') IS NULL OR OBJECT_ID('dbo.sp_RenewInboxLease','P') IS NULL THROW 51708,'Inbox worker contract incomplete.',1;
PRINT 'V1 RC4 POSTFLIGHT: PASS';
GO