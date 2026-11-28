-- CUSTOMER-PORTAL-FOUNDATION - READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/070-customer-invoice-templates.sql. Expected last line: VERIFY_STATUS=PASS (NOT_APPLIED before the migration).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
PRINT N'=== CUSTOMER INVOICE TEMPLATES VERIFY ===';
DECLARE @t int = OBJECT_ID(N'dbo.CustomerInvoiceTemplates', N'U');
IF @t IS NULL
BEGIN
    PRINT N'INFO: dbo.CustomerInvoiceTemplates missing';
    PRINT N'VERIFY_STATUS=NOT_APPLIED';
    RETURN;
END
DECLARE @fail int = 0;
PRINT N'PASS: dbo.CustomerInvoiceTemplates exists';
IF (SELECT COUNT(*) FROM sys.columns WHERE object_id = @t AND name IN (N'Id',N'CustomerId',N'Provider',N'Environment',N'InvoicePattern',N'InvoiceSeries',N'CreatedAt',N'CreatedBy')) = 8
    PRINT N'PASS: columns'; ELSE BEGIN PRINT N'FAIL: columns incomplete'; SET @fail += 1; END
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = @t AND name = N'CK_CIT_Environment' AND is_disabled = 0 AND is_not_trusted = 0)
    PRINT N'PASS: CK_CIT_Environment (enabled, trusted)'; ELSE BEGIN PRINT N'FAIL: CK_CIT_Environment missing, disabled or untrusted'; SET @fail += 1; END
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = @t AND name = N'FK_CIT_Customer' AND is_disabled = 0)
    PRINT N'PASS: FK_CIT_Customer'; ELSE BEGIN PRINT N'FAIL: FK_CIT_Customer missing'; SET @fail += 1; END
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = @t AND name = N'UX_CIT_Template' AND is_unique = 1)
    PRINT N'PASS: UX_CIT_Template'; ELSE BEGIN PRINT N'FAIL: UX_CIT_Template missing'; SET @fail += 1; END
PRINT N'VERIFY_STATUS=' + CASE WHEN @fail = 0 THEN N'PASS' ELSE N'FAIL' END;
