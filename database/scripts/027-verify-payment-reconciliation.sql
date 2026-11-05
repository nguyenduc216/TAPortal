-- TINGEE-INTEGRATION — READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/019-payment-reconciliation.sql. Expected last line: VERIFY_STATUS=PASS (NOT_APPLIED before the migration).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
DECLARE @fail int = 0;
PRINT N'=== PAYMENT RECONCILIATION VERIFY ===';
IF OBJECT_ID(N'dbo.PaymentTransactions', N'U') IS NULL AND COL_LENGTH(N'dbo.CustomerOrders', N'PartnerPaidAmount') IS NULL
   AND NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_CO_Status' AND definition LIKE N'%EXPIRED_PAID%')
   AND NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_TWI_Status' AND definition LIKE N'%UNMATCHED%')
BEGIN
    PRINT N'INFO: nothing applied';
    PRINT N'VERIFY_STATUS=NOT_APPLIED';
    RETURN;
END
IF COL_LENGTH(N'dbo.CustomerOrders', N'PartnerPaidAmount') IS NOT NULL AND COL_LENGTH(N'dbo.CustomerOrders', N'PartnerCheckedAt') IS NOT NULL
    PRINT N'PASS: CustomerOrders partner columns'; ELSE BEGIN PRINT N'FAIL: CustomerOrders partner columns missing'; SET @fail += 1; END
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.CustomerOrders') AND name = N'CK_CO_Status'
           AND definition LIKE N'%EXPIRED_PAID%' AND definition LIKE N'%CANCELLED_PAID%' AND is_disabled = 0 AND is_not_trusted = 0)
    PRINT N'PASS: CK_CO_Status widened'; ELSE BEGIN PRINT N'FAIL: CK_CO_Status not widened/trusted'; SET @fail += 1; END
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.TingeeWebhookInbox') AND name = N'CK_TWI_Status'
           AND definition LIKE N'%UNMATCHED%' AND is_disabled = 0 AND is_not_trusted = 0)
    PRINT N'PASS: CK_TWI_Status widened'; ELSE BEGIN PRINT N'FAIL: CK_TWI_Status not widened/trusted'; SET @fail += 1; END
IF (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.PaymentTransactions') AND name IN
    (N'Id',N'InboxId',N'Direction',N'TransactionCode',N'Amount',N'Content',N'Bank',N'AccountNumber',N'VaAccountNumber',N'TransactionAt',N'ReceivedAt',
     N'CustomerId',N'OrderId',N'Status',N'Flags',N'Reason',N'MatchedBy',N'SystemPaidBefore',N'SystemPaidAfter',N'PartnerPaidTotal',N'OrderStatusAfter',
     N'ResolvedAt',N'ResolvedBy',N'ResolvedVia',N'ResolutionNote',N'NotifiedAt',N'CreatedAt',N'UpdatedAt')) = 28
    PRINT N'PASS: dbo.PaymentTransactions 28 columns'; ELSE BEGIN PRINT N'FAIL: dbo.PaymentTransactions columns missing'; SET @fail += 1; END
IF (SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.PaymentTransactions') AND name IN (N'UX_PT_Inbox',N'IX_PT_Status',N'IX_PT_Customer',N'IX_PT_Order')) = 4
    PRINT N'PASS: 4 indexes'; ELSE BEGIN PRINT N'FAIL: PaymentTransactions indexes missing'; SET @fail += 1; END
IF (SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.PaymentTransactions')
    AND name IN (N'CK_PT_Direction',N'CK_PT_Status',N'CK_PT_Via',N'CK_PT_Amount') AND is_disabled = 0) = 4
    PRINT N'PASS: 4 check constraints'; ELSE BEGIN PRINT N'FAIL: PaymentTransactions check constraints missing'; SET @fail += 1; END
IF (SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'dbo.PaymentTransactions') AND name IN (N'FK_PT_Inbox',N'FK_PT_Customer',N'FK_PT_Order')) = 3
    PRINT N'PASS: 3 foreign keys'; ELSE BEGIN PRINT N'FAIL: PaymentTransactions foreign keys missing'; SET @fail += 1; END
PRINT N'VERIFY_STATUS=' + CASE WHEN @fail = 0 THEN N'PASS' ELSE N'FAIL' END;
