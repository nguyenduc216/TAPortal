/* TAPortal 025 - Operational/reconciliation reporting views. */
USE [TAPortal];
GO
CREATE OR ALTER VIEW dbo.vw_PartnerBankingDashboard AS
SELECT p.Id PartnerId,p.Code PartnerCode,p.Name PartnerName,
 COUNT(DISTINCT a.Id) BankAccountCount,
 COUNT(DISTINCT CASE WHEN a.BankApiConnected=1 THEN a.Id END) ConnectedBankAccountCount,
 COUNT(DISTINCT CASE WHEN q.Status IN('PENDING','PARTIALLY_PAID','NEEDS_REVIEW') THEN q.Id END) OpenPaymentRequestCount,
 CAST(ISNULL(SUM(CASE WHEN q.Status IN('PENDING','PARTIALLY_PAID','NEEDS_REVIEW') THEN CASE WHEN q.AmountDue>q.AmountPaid THEN q.AmountDue-q.AmountPaid ELSE 0 END ELSE 0 END),0) AS decimal(18,2)) OutstandingAmount
FROM dbo.Partners p LEFT JOIN dbo.PartnerBankAccounts a ON a.PartnerId=p.Id LEFT JOIN dbo.PaymentRequests q ON q.PartnerId=p.Id
WHERE p.IsDeleted=0 GROUP BY p.Id,p.Code,p.Name;
GO
CREATE OR ALTER VIEW dbo.vw_UnresolvedBankTransactions AS
SELECT t.Id,t.PartnerId,t.TransactionDate,t.Amount,t.PaymentCode,t.Content,t.ExternalTransactionId,s.AllocatedAmount,s.UnallocatedAmount,s.AllocationStatus,
 CASE WHEN EXISTS(SELECT 1 FROM dbo.BankTransactionReviewQueue r WHERE r.BankTransactionId=t.Id AND r.Status='OPEN') THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END HasOpenReview
FROM dbo.BankTransactions t JOIN dbo.vw_BankTransactionAllocationSummary s ON s.Id=t.Id WHERE t.Direction='CREDIT' AND s.UnallocatedAmount>0;
GO
CREATE OR ALTER VIEW dbo.vw_InvoiceOperations AS
SELECT r.Id InvoiceRequestId,r.PartnerId,r.CustomerId,r.PaymentRequestId,r.Ikey,r.RequestedAmount,r.Status RequestStatus,r.ValidationStatus,i.Id InvoiceId,i.InvoiceNo,i.ExternalInvoiceId,i.Status InvoiceStatus,i.IssuedAt,r.RequestedAt
FROM dbo.InvoiceRequests r LEFT JOIN dbo.Invoices i ON i.InvoiceRequestId=r.Id;
GO
CREATE OR ALTER VIEW dbo.vw_ProviderConnectionHealth AS
SELECT c.Id ConnectionId,c.PartnerId,p.Code ProviderCode,c.Environment,c.Status,c.LastSyncedAt,
 (SELECT MAX(l.CreatedAt) FROM dbo.ProviderApiLogs l WHERE l.PartnerProviderConnectionId=c.Id AND l.Success=1) LastSuccessAt,
 (SELECT MAX(l.CreatedAt) FROM dbo.ProviderApiLogs l WHERE l.PartnerProviderConnectionId=c.Id AND l.Success=0) LastFailureAt
FROM dbo.PartnerProviderConnections c JOIN dbo.Providers p ON p.Id=c.ProviderId;
GO
PRINT '025-operational-reporting-views.sql: OK';
GO