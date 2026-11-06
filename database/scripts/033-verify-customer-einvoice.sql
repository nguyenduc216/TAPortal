-- CUSTOMER-PORTAL-FOUNDATION — READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/022-customer-einvoice.sql. Expected last line: VERIFY_STATUS=PASS (NOT_APPLIED before the migration).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
DECLARE @fail int = 0;
DECLARE @table bit = CASE WHEN OBJECT_ID(N'dbo.CustomerInvoiceSettings', N'U') IS NULL THEN 0 ELSE 1 END;
DECLARE @cols int = (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.CustomerOrders') AND name IN
    (N'InvoiceCode',N'InvoiceNumber',N'InvoiceLookupCode',N'InvoiceTrackingCode',N'InvoiceIssuedAt',N'InvoiceEmailedAt',N'InvoiceError',N'InvoiceAttempts',N'InvoiceNextAttemptAt',N'InvoiceLockedUntil'));
PRINT N'=== CUSTOMER E-INVOICE VERIFY ===';
IF @table = 0 AND @cols = 0
BEGIN
    PRINT N'INFO: e-invoice settings table and order invoice columns missing';
    PRINT N'VERIFY_STATUS=NOT_APPLIED';
    RETURN;
END
IF @table = 1 PRINT N'PASS: dbo.CustomerInvoiceSettings'; ELSE BEGIN PRINT N'FAIL: dbo.CustomerInvoiceSettings missing'; SET @fail += 1; END
IF (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.CustomerInvoiceSettings') AND name IN
    (N'CustomerId',N'Provider',N'ProviderAccountId',N'ProviderTaxCode',N'ProviderUsername',N'InvoicePattern',N'InvoiceSeries',N'AutoIssueEnabled',N'SendEmail',N'AutoIssueFrom',N'ConnectedAt',N'UpdatedAt',N'UpdatedBy')) = 13
    PRINT N'PASS: dbo.CustomerInvoiceSettings 13 columns'; ELSE BEGIN PRINT N'FAIL: dbo.CustomerInvoiceSettings columns'; SET @fail += 1; END
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.CustomerInvoiceSettings') AND name LIKE N'%Password%')
    PRINT N'PASS: no password column'; ELSE BEGIN PRINT N'FAIL: a password column exists'; SET @fail += 1; END
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.CustomerInvoiceSettings') AND name = N'CK_CIS_Provider' AND is_disabled = 0)
    PRINT N'PASS: CK_CIS_Provider'; ELSE BEGIN PRINT N'FAIL: CK_CIS_Provider missing'; SET @fail += 1; END
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'dbo.CustomerInvoiceSettings') AND name = N'FK_CIS_Customer')
    PRINT N'PASS: FK_CIS_Customer'; ELSE BEGIN PRINT N'FAIL: FK_CIS_Customer missing'; SET @fail += 1; END
IF @cols = 10 PRINT N'PASS: dbo.CustomerOrders 10 invoice columns';
ELSE BEGIN PRINT N'FAIL: dbo.CustomerOrders has ' + CAST(@cols AS nvarchar(5)) + N' of 10 invoice columns'; SET @fail += 1; END
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.CustomerOrders') AND name = N'UX_CO_InvoiceCode' AND is_unique = 1 AND has_filter = 1)
    PRINT N'PASS: UX_CO_InvoiceCode'; ELSE BEGIN PRINT N'FAIL: UX_CO_InvoiceCode missing'; SET @fail += 1; END
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.CustomerOrders') AND name = N'IX_CO_InvoiceQueue')
    PRINT N'PASS: IX_CO_InvoiceQueue'; ELSE BEGIN PRINT N'FAIL: IX_CO_InvoiceQueue missing'; SET @fail += 1; END
PRINT N'VERIFY_STATUS=' + CASE WHEN @fail = 0 THEN N'PASS' ELSE N'FAIL' END;
