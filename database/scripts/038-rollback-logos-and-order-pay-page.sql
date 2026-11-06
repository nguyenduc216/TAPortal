-- CUSTOMER-PORTAL-FOUNDATION — ROLLBACK of migrations/024-logos-and-order-pay-page.sql.
-- Drops dbo.CustomerLogos and the 4 payment page columns of dbo.CustomerOrders. Refuses when customers have uploaded a
-- logo or a payer corrected invoice details (customer data): export and decide with the operator first. Page tokens alone
-- do not block it; existing payment page links stop working.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @used int = 0;
IF OBJECT_ID(N'dbo.CustomerLogos', N'U') IS NOT NULL
    EXEC sys.sp_executesql N'SELECT @n = COUNT(*) FROM dbo.CustomerLogos', N'@n int OUTPUT', @n = @used OUTPUT;
IF @used > 0
    THROW 51002, 'Rollback refused: dbo.CustomerLogos holds uploaded logos; export them first', 1;
IF COL_LENGTH(N'dbo.CustomerOrders', N'InvoiceBuyerName') IS NOT NULL AND COL_LENGTH(N'dbo.CustomerOrders', N'InvoiceBuyerTaxCode') IS NOT NULL
    EXEC sys.sp_executesql N'SELECT @n = COUNT(*) FROM dbo.CustomerOrders WHERE InvoiceBuyerName IS NOT NULL OR InvoiceBuyerTaxCode IS NOT NULL', N'@n int OUTPUT', @n = @used OUTPUT;
IF @used > 0
    THROW 51003, 'Rollback refused: orders hold invoice details entered by payers; export them first', 1;

IF OBJECT_ID(N'dbo.CustomerLogos', N'U') IS NOT NULL DROP TABLE dbo.CustomerLogos;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.CustomerOrders') AND name = N'UX_CustomerOrders_PublicToken')
    DROP INDEX UX_CustomerOrders_PublicToken ON dbo.CustomerOrders;
IF COL_LENGTH(N'dbo.CustomerOrders', N'PublicToken') IS NOT NULL ALTER TABLE dbo.CustomerOrders DROP COLUMN PublicToken;
IF COL_LENGTH(N'dbo.CustomerOrders', N'InvoiceBuyerTaxCode') IS NOT NULL ALTER TABLE dbo.CustomerOrders DROP COLUMN InvoiceBuyerTaxCode;
IF COL_LENGTH(N'dbo.CustomerOrders', N'InvoiceBuyerName') IS NOT NULL ALTER TABLE dbo.CustomerOrders DROP COLUMN InvoiceBuyerName;
IF COL_LENGTH(N'dbo.CustomerOrders', N'InvoiceBuyerUpdatedAt') IS NOT NULL ALTER TABLE dbo.CustomerOrders DROP COLUMN InvoiceBuyerUpdatedAt;
COMMIT;
PRINT N'038-rollback-logos-and-order-pay-page.sql: OK';
