-- CUSTOMER-PORTAL-FOUNDATION — READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/023-einvoice-template-id.sql. Expected last line: VERIFY_STATUS=PASS (NOT_APPLIED before the migration).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
DECLARE @len int = COL_LENGTH(N'dbo.CustomerInvoiceSettings', N'InvoicePattern');
PRINT N'=== E-INVOICE TEMPLATE ID VERIFY ===';
IF @len IS NULL
BEGIN
    PRINT N'FAIL: dbo.CustomerInvoiceSettings.InvoicePattern missing (022 not applied)';
    PRINT N'VERIFY_STATUS=FAIL';
    RETURN;
END
IF @len = 20
BEGIN
    PRINT N'INFO: InvoicePattern is varchar(20)';
    PRINT N'VERIFY_STATUS=NOT_APPLIED';
    RETURN;
END
IF @len = 50 AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.CustomerInvoiceSettings') AND name = N'InvoicePattern' AND is_nullable = 1)
BEGIN
    PRINT N'PASS: InvoicePattern varchar(50) NULL';
    PRINT N'VERIFY_STATUS=PASS';
END
ELSE
BEGIN
    PRINT N'FAIL: InvoicePattern length ' + CAST(@len AS nvarchar(10));
    PRINT N'VERIFY_STATUS=FAIL';
END
