-- TINGEE-INTEGRATION - READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/069-payment-collections.sql. Expected last line: VERIFY_STATUS=PASS (NOT_APPLIED before the migration).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
PRINT N'=== PAYMENT COLLECTIONS VERIFY ===';
DECLARE @pc int = OBJECT_ID(N'dbo.PaymentCollections', N'U'), @pco int = OBJECT_ID(N'dbo.PaymentCollectionOrders', N'U'),
        @pa int = OBJECT_ID(N'dbo.PaymentAllocations', N'U'), @col bit = CASE WHEN COL_LENGTH(N'dbo.PaymentTransactions', N'CollectionId') IS NULL THEN 0 ELSE 1 END;
IF @pc IS NULL AND @pco IS NULL AND @pa IS NULL AND @col = 0
BEGIN
    PRINT N'INFO: no payment collection table and no PaymentTransactions.CollectionId';
    PRINT N'VERIFY_STATUS=NOT_APPLIED';
    RETURN;
END
DECLARE @fail int = 0;
IF @pc IS NOT NULL PRINT N'PASS: dbo.PaymentCollections exists'; ELSE BEGIN PRINT N'FAIL: dbo.PaymentCollections missing'; SET @fail += 1; END
IF @pco IS NOT NULL PRINT N'PASS: dbo.PaymentCollectionOrders exists'; ELSE BEGIN PRINT N'FAIL: dbo.PaymentCollectionOrders missing'; SET @fail += 1; END
IF @pa IS NOT NULL PRINT N'PASS: dbo.PaymentAllocations exists'; ELSE BEGIN PRINT N'FAIL: dbo.PaymentAllocations missing'; SET @fail += 1; END
IF @col = 1 PRINT N'PASS: dbo.PaymentTransactions.CollectionId exists'; ELSE BEGIN PRINT N'FAIL: dbo.PaymentTransactions.CollectionId missing'; SET @fail += 1; END
IF @pc IS NOT NULL
BEGIN
    IF (SELECT COUNT(*) FROM sys.columns WHERE object_id = @pc AND name IN (N'CustomerId',N'CollectionNo',N'ContactId',N'BankAccountId',N'Amount',N'PaidAmount',N'Status',
        N'QrType',N'QrPayload',N'QrAccount',N'BillId',N'QrExpireMinutes',N'ExpiresAt',N'PublicToken',N'PartnerPaidAmount',N'LastError',N'PaidAt',N'CancelledAt')) = 18
        PRINT N'PASS: dbo.PaymentCollections columns'; ELSE BEGIN PRINT N'FAIL: dbo.PaymentCollections columns incomplete'; SET @fail += 1; END
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = @pc AND name = N'CK_PC_Status' AND is_disabled = 0 AND is_not_trusted = 0
               AND definition LIKE N'%PARTIALLY_PAID%' AND definition LIKE N'%CANCELLED%')
        PRINT N'PASS: CK_PC_Status (enabled, trusted)'; ELSE BEGIN PRINT N'FAIL: CK_PC_Status missing, disabled or untrusted'; SET @fail += 1; END
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = @pc AND name = N'UX_PC_Customer_No' AND is_unique = 1)
       AND EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = @pc AND name = N'UX_PC_BillId' AND is_unique = 1 AND has_filter = 1)
       AND EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = @pc AND name = N'UX_PC_PublicToken' AND is_unique = 1 AND has_filter = 1)
        PRINT N'PASS: unique number, bill and page token indexes'; ELSE BEGIN PRINT N'FAIL: dbo.PaymentCollections unique indexes missing'; SET @fail += 1; END
END
IF @pa IS NOT NULL
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = @pa AND name = N'CK_PA_Amount' AND is_disabled = 0 AND is_not_trusted = 0)
        PRINT N'PASS: CK_PA_Amount (enabled, trusted)'; ELSE BEGIN PRINT N'FAIL: CK_PA_Amount missing, disabled or untrusted'; SET @fail += 1; END
IF @col = 1
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'dbo.PaymentTransactions') AND name = N'FK_PT_Collection' AND is_disabled = 0)
       AND EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.PaymentTransactions') AND name = N'IX_PT_Collection')
        PRINT N'PASS: FK_PT_Collection and IX_PT_Collection'; ELSE BEGIN PRINT N'FAIL: FK_PT_Collection or IX_PT_Collection missing'; SET @fail += 1; END
PRINT N'VERIFY_STATUS=' + CASE WHEN @fail = 0 THEN N'PASS' ELSE N'FAIL' END;
