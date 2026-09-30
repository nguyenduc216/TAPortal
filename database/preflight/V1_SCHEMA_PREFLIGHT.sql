/* READ-ONLY preflight for T.A Portal Schema V1 candidate. Does not mutate data. */
SET NOCOUNT ON;
SELECT DB_NAME() DatabaseName,SERVERPROPERTY('ProductVersion') SqlVersion,SERVERPROPERTY('Edition') Edition;
SELECT 'ORPHAN_COMPANY_PARTNER' CheckName,COUNT(*) ProblemCount FROM dbo.Companies c LEFT JOIN dbo.Partners p ON p.Id=c.PartnerId WHERE c.PartnerId IS NOT NULL AND p.Id IS NULL
UNION ALL SELECT 'ORPHAN_CUSTOMER_PARTNER',COUNT(*) FROM dbo.Customers c LEFT JOIN dbo.Partners p ON p.Id=c.PartnerId WHERE c.PartnerId IS NOT NULL AND p.Id IS NULL
UNION ALL SELECT 'DUP_PAYMENT_CODE',COUNT(*) FROM (SELECT PartnerId,PaymentCodeNormalized FROM dbo.PaymentRequests GROUP BY PartnerId,PaymentCodeNormalized HAVING COUNT(*)>1) x
UNION ALL SELECT 'NEGATIVE_WALLET_AVAILABLE',COUNT(*) FROM dbo.PartnerCreditWallets WHERE CurrentBalance<0 OR ReservedBalance<0 OR ReservedBalance>CurrentBalance
UNION ALL SELECT 'OVER_ALLOCATED_TRANSACTION',COUNT(*) FROM dbo.vw_BankTransactionAllocationSummary WHERE AllocationStatus='OVER_ALLOCATED'
UNION ALL SELECT 'PAYMENT_CACHE_MISMATCH',COUNT(*) FROM dbo.PaymentRequests p JOIN dbo.vw_PaymentRequestBalances v ON v.Id=p.Id WHERE p.AmountPaid<>v.AmountPaid;
SELECT o.type_desc,o.name FROM sys.objects o WHERE o.name IN('Partners','PartnerProviderConnections','PartnerBankAccounts','BankTransactions','PaymentRequests','PaymentAllocations','PartnerCreditWallets','CreditLedger','InvoiceRequests','Invoices','OutboxMessages','SchemaVersions') ORDER BY o.name;
