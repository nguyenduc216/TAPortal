/* RC2 034 - Backfill generic payment transactions and make allocation source rail-neutral. */
USE [TAPortal]; GO
INSERT dbo.PaymentTransactions(Id,PartnerId,CompanyId,ProviderId,FinancialAccountId,RailType,ExternalTransactionId,TransactionAt,Direction,Amount,CurrencyCode,ReferenceNumber,Content,PaymentCode,CounterpartyReference,Source,CreatedAt)
SELECT NEWID(),b.PartnerId,a.CompanyId,b.ProviderId,NULL,'BANK',b.ExternalTransactionId,b.TransactionDate,b.Direction,b.Amount,'VND',b.ReferenceNumber,b.Content,b.PaymentCode,
 COALESCE(b.CounterpartyAccount,b.CounterpartyName),b.Source,b.CreatedAt
FROM dbo.BankTransactions b JOIN dbo.PartnerBankAccounts a ON a.Id=b.BankAccountId
WHERE b.PaymentTransactionId IS NULL AND NOT EXISTS(SELECT 1 FROM dbo.PaymentTransactions p WHERE p.ProviderId=b.ProviderId AND p.ExternalTransactionId=b.ExternalTransactionId);
UPDATE b SET PaymentTransactionId=p.Id FROM dbo.BankTransactions b JOIN dbo.PaymentTransactions p ON p.ProviderId=b.ProviderId AND p.ExternalTransactionId=b.ExternalTransactionId WHERE b.PaymentTransactionId IS NULL;
GO
IF COL_LENGTH('dbo.PaymentAllocations','PaymentTransactionId') IS NULL ALTER TABLE dbo.PaymentAllocations ADD PaymentTransactionId uniqueidentifier NULL;
GO
UPDATE a SET PaymentTransactionId=b.PaymentTransactionId FROM dbo.PaymentAllocations a JOIN dbo.BankTransactions b ON b.Id=a.BankTransactionId WHERE a.PaymentTransactionId IS NULL;
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name='FK_PA_PaymentTransaction') ALTER TABLE dbo.PaymentAllocations ADD CONSTRAINT FK_PA_PaymentTransaction FOREIGN KEY(PaymentTransactionId) REFERENCES dbo.PaymentTransactions(Id);
GO
IF EXISTS(SELECT 1 FROM dbo.PaymentAllocations WHERE PaymentTransactionId IS NULL) THROW 51340,'Cannot finalize generic allocation: unresolved payment transaction.',1;
ALTER TABLE dbo.PaymentAllocations ALTER COLUMN PaymentTransactionId uniqueidentifier NOT NULL;
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.PaymentAllocations') AND name='IX_PA_PaymentTransaction') CREATE INDEX IX_PA_PaymentTransaction ON dbo.PaymentAllocations(PaymentTransactionId,Status) INCLUDE(PaymentRequestId,AllocatedAmount);
GO
PRINT '034-payment-allocation-generic-source.sql: OK'; GO