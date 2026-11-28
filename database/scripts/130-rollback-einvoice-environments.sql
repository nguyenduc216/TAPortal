-- CUSTOMER-PORTAL-FOUNDATION - ROLLBACK of migrations/071-einvoice-environments.sql.
-- Drops the per-environment connections and CustomerInvoiceTemplates.IsDefault. The connection in use
-- (dbo.CustomerInvoiceSettings) is not touched; a saved login of the other environment is lost and must be entered again.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.CustomerInvoiceConnections', N'U') IS NOT NULL DROP TABLE dbo.CustomerInvoiceConnections;
IF OBJECT_ID(N'dbo.DF_CIT_IsDefault', N'D') IS NOT NULL ALTER TABLE dbo.CustomerInvoiceTemplates DROP CONSTRAINT DF_CIT_IsDefault;
IF COL_LENGTH(N'dbo.CustomerInvoiceTemplates', N'IsDefault') IS NOT NULL ALTER TABLE dbo.CustomerInvoiceTemplates DROP COLUMN IsDefault;
COMMIT;
PRINT N'130-rollback-einvoice-environments.sql: OK';
