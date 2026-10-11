/* T.A Portal V1 RC2 READ-ONLY preflight. SQL Server 2014 compatible. */
SET NOCOUNT ON;
SELECT DB_NAME() DatabaseName,SERVERPROPERTY('ProductVersion') SqlVersion,SERVERPROPERTY('ProductLevel') ProductLevel,SERVERPROPERTY('Edition') Edition;
SELECT 'COMPANY_PARTNER_ORPHAN' CheckName,COUNT(*) ProblemCount FROM dbo.Companies c LEFT JOIN dbo.Partners p ON p.Id=c.PartnerId WHERE c.PartnerId IS NOT NULL AND p.Id IS NULL
UNION ALL SELECT 'CUSTOMER_PARTNER_ORPHAN',COUNT(*) FROM dbo.Customers c LEFT JOIN dbo.Partners p ON p.Id=c.PartnerId WHERE c.PartnerId IS NOT NULL AND p.Id IS NULL
UNION ALL SELECT 'PAYMENT_CODE_COLLISION',COUNT(*) FROM (SELECT PartnerId,NormalizedCode FROM dbo.PaymentCodeRegistry GROUP BY PartnerId,NormalizedCode HAVING COUNT(*)>1)x
UNION ALL SELECT 'WALLET_INVARIANT',COUNT(*) FROM dbo.PartnerCreditWallets WHERE CurrentBalance<0 OR ReservedBalance<0 OR ReservedBalance>CurrentBalance
UNION ALL SELECT 'CROSS_TENANT_ALLOCATION',COUNT(*) FROM dbo.PaymentAllocations a JOIN dbo.PaymentRequests r ON r.Id=a.PaymentRequestId JOIN dbo.PaymentTransactions t ON t.Id=a.PaymentTransactionId WHERE a.PartnerId<>r.PartnerId OR a.PartnerId<>t.PartnerId
UNION ALL SELECT 'ALLOCATION_OVER_TRANSACTION',COUNT(*) FROM (SELECT a.PaymentTransactionId FROM dbo.PaymentAllocations a JOIN dbo.PaymentTransactions t ON t.Id=a.PaymentTransactionId WHERE a.Status='ACTIVE' GROUP BY a.PaymentTransactionId,t.Amount HAVING SUM(a.AllocatedAmount)>t.Amount)x
UNION ALL SELECT 'AMOUNT_ONLY_AUTO_MATCH',COUNT(*) FROM dbo.PaymentMatchingRules WHERE RuleType='AMOUNT_ASSIST' AND AutoAllocate=1
UNION ALL SELECT 'INVOICE_DUPLICATE_IKEY',COUNT(*) FROM (SELECT PartnerId,Ikey FROM dbo.InvoiceRequests GROUP BY PartnerId,Ikey HAVING COUNT(*)>1)x
UNION ALL SELECT 'PLAINTEXT_HOSTED_LINK',COUNT(*) FROM dbo.BankLinkSessions WHERE HostedLinkUrl IS NOT NULL;
SELECT 'RC2_REQUIRED_OBJECT_MISSING' CheckName,v.ObjectName FROM (VALUES('PartnerRoles'),('FinancialAccounts'),('PaymentTransactions'),('PaymentInstructions'),('PaymentCodeRegistry'),('CustomerCashAccounts'),('CustomerCashLedger'),('RefundRequests'),('MatchingAttempts'),('MatchingEvidence'),('CreditDefinitions'),('SubscriptionCreditGrants'),('InvoiceCreditConsumptions'),('InboxMessages'))v(ObjectName) WHERE OBJECT_ID('dbo.'+v.ObjectName,'U') IS NULL;
