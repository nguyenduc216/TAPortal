-- CUSTOMER-PORTAL-FOUNDATION - ROLLBACK of migrations/070-customer-invoice-templates.sql.
-- Drops the saved template list. The default template (dbo.CustomerInvoiceSettings) is not touched, so nothing an order needs is lost.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.CustomerInvoiceTemplates', N'U') IS NOT NULL DROP TABLE dbo.CustomerInvoiceTemplates;
COMMIT;
PRINT N'128-rollback-customer-invoice-templates.sql: OK';
