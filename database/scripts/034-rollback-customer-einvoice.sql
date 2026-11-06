-- CUSTOMER-PORTAL-FOUNDATION — ROLLBACK of migrations/022-customer-einvoice.sql.
-- Drops dbo.CustomerInvoiceSettings and the 10 invoice columns of dbo.CustomerOrders. Refuses when a company has saved
-- e-invoice settings or an order has an invoice code (an invoice may have been issued): export and decide with the operator first.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @used int = 0;
IF OBJECT_ID(N'dbo.CustomerInvoiceSettings', N'U') IS NOT NULL
    EXEC sys.sp_executesql N'SELECT @n = COUNT(*) FROM dbo.CustomerInvoiceSettings', N'@n int OUTPUT', @n = @used OUTPUT;
IF @used > 0
    THROW 51002, 'Rollback refused: dbo.CustomerInvoiceSettings holds company settings; export them first', 1;
IF COL_LENGTH(N'dbo.CustomerOrders', N'InvoiceCode') IS NOT NULL
BEGIN
    EXEC sys.sp_executesql N'SELECT @n = COUNT(*) FROM dbo.CustomerOrders WHERE InvoiceCode IS NOT NULL', N'@n int OUTPUT', @n = @used OUTPUT;
    IF @used > 0
        THROW 51003, 'Rollback refused: orders carry invoice codes (invoices may have been issued); export them first', 1;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.CustomerOrders') AND name = N'UX_CO_InvoiceCode') DROP INDEX UX_CO_InvoiceCode ON dbo.CustomerOrders;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.CustomerOrders') AND name = N'IX_CO_InvoiceQueue') DROP INDEX IX_CO_InvoiceQueue ON dbo.CustomerOrders;
    IF OBJECT_ID(N'dbo.DF_CO_InvoiceAttempts', N'D') IS NOT NULL ALTER TABLE dbo.CustomerOrders DROP CONSTRAINT DF_CO_InvoiceAttempts;
    ALTER TABLE dbo.CustomerOrders DROP COLUMN InvoiceCode, InvoiceNumber, InvoiceLookupCode, InvoiceTrackingCode, InvoiceIssuedAt, InvoiceEmailedAt,
        InvoiceError, InvoiceAttempts, InvoiceNextAttemptAt, InvoiceLockedUntil;
END
IF OBJECT_ID(N'dbo.CustomerInvoiceSettings', N'U') IS NOT NULL DROP TABLE dbo.CustomerInvoiceSettings;
COMMIT;
PRINT N'034-rollback-customer-einvoice.sql: OK';
