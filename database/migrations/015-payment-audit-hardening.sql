/* TAPortal 015 - Payment audit/reconciliation hardening. */
USE [TAPortal];
GO
IF OBJECT_ID(N'dbo.BankWebhookTransactionLinks',N'U') IS NULL BEGIN
 CREATE TABLE dbo.BankWebhookTransactionLinks(WebhookEventId bigint NOT NULL,BankTransactionId uniqueidentifier NOT NULL,LinkType varchar(20) NOT NULL CONSTRAINT DF_BWTL_Type DEFAULT 'CREATED_OR_FOUND',LinkedAt datetime2(3) NOT NULL CONSTRAINT DF_BWTL_Linked DEFAULT SYSUTCDATETIME(),CONSTRAINT PK_BankWebhookTransactionLinks PRIMARY KEY(WebhookEventId,BankTransactionId),CONSTRAINT FK_BWTL_Event FOREIGN KEY(WebhookEventId) REFERENCES dbo.BankWebhookEvents(Id),CONSTRAINT FK_BWTL_Transaction FOREIGN KEY(BankTransactionId) REFERENCES dbo.BankTransactions(Id));
 CREATE INDEX IX_BWTL_Transaction ON dbo.BankWebhookTransactionLinks(BankTransactionId,LinkedAt DESC);
END
GO
IF OBJECT_ID(N'dbo.vw_PaymentRequestBalances',N'V') IS NULL EXEC('CREATE VIEW dbo.vw_PaymentRequestBalances AS SELECT 1 AS Placeholder');
GO
ALTER VIEW dbo.vw_PaymentRequestBalances AS
SELECT p.Id,p.PartnerId,p.CustomerId,p.Code,p.PaymentCode,p.Description,p.AmountDue,
 CAST(ISNULL(SUM(CASE WHEN a.Status='ACTIVE' THEN a.AllocatedAmount ELSE 0 END),0) AS decimal(18,2)) AS AmountPaid,
 CAST(CASE WHEN p.AmountDue-ISNULL(SUM(CASE WHEN a.Status='ACTIVE' THEN a.AllocatedAmount ELSE 0 END),0)>0 THEN p.AmountDue-ISNULL(SUM(CASE WHEN a.Status='ACTIVE' THEN a.AllocatedAmount ELSE 0 END),0) ELSE 0 END AS decimal(18,2)) AS AmountRemaining,
 CAST(CASE WHEN ISNULL(SUM(CASE WHEN a.Status='ACTIVE' THEN a.AllocatedAmount ELSE 0 END),0)-p.AmountDue>0 THEN ISNULL(SUM(CASE WHEN a.Status='ACTIVE' THEN a.AllocatedAmount ELSE 0 END),0)-p.AmountDue ELSE 0 END AS decimal(18,2)) AS OverpaidAmount,
 CASE WHEN p.Status IN('CANCELLED','EXPIRED','NEEDS_REVIEW') THEN p.Status WHEN ISNULL(SUM(CASE WHEN a.Status='ACTIVE' THEN a.AllocatedAmount ELSE 0 END),0)=0 THEN 'PENDING' WHEN ISNULL(SUM(CASE WHEN a.Status='ACTIVE' THEN a.AllocatedAmount ELSE 0 END),0)<p.AmountDue THEN 'PARTIALLY_PAID' WHEN ISNULL(SUM(CASE WHEN a.Status='ACTIVE' THEN a.AllocatedAmount ELSE 0 END),0)=p.AmountDue THEN 'PAID' ELSE 'OVERPAID' END AS CalculatedStatus,
 p.ExpiresAt,p.ClosedAt,p.CreatedAt,p.UpdatedAt
FROM dbo.PaymentRequests p LEFT JOIN dbo.PaymentAllocations a ON a.PaymentRequestId=p.Id
GROUP BY p.Id,p.PartnerId,p.CustomerId,p.Code,p.PaymentCode,p.Description,p.AmountDue,p.Status,p.ExpiresAt,p.ClosedAt,p.CreatedAt,p.UpdatedAt;
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.BankWebhookEvents') AND name='IX_BWE_ProviderExternalEvent') CREATE INDEX IX_BWE_ProviderExternalEvent ON dbo.BankWebhookEvents(ProviderId,ExternalEventId,ReceivedAt DESC) WHERE ExternalEventId IS NOT NULL;
GO
PRINT '015-payment-audit-hardening.sql: OK';
GO