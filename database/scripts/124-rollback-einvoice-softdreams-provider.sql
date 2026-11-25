-- TINGEE-INTEGRATION - ROLLBACK of migrations/068-einvoice-softdreams-provider.sql.
-- Restores CK_CIS_Provider to Provider IN ('misa','s-invoice') and drops ProviderSecret. Refuses once a company has connected
-- SoftDreams EasyInvoice or a login is stored (that row would violate the constraint, and dropping the column would lose the
-- login): decide with the operator first.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.CustomerInvoiceSettings', N'U') IS NULL
 THROW 51001, 'Precondition failed: dbo.CustomerInvoiceSettings missing', 1;
IF EXISTS (SELECT 1 FROM dbo.CustomerInvoiceSettings WHERE Provider NOT IN ('misa','s-invoice'))
    THROW 51002, 'Rollback refused: companies use SoftDreams EasyInvoice', 1;
IF COL_LENGTH(N'dbo.CustomerInvoiceSettings', N'ProviderSecret') IS NOT NULL
    EXEC sys.sp_executesql N'IF EXISTS (SELECT 1 FROM dbo.CustomerInvoiceSettings WHERE ProviderSecret IS NOT NULL) THROW 51003, ''Rollback refused: stored provider logins exist'', 1;';
IF OBJECT_ID(N'dbo.CK_CIS_Provider', N'C') IS NOT NULL ALTER TABLE dbo.CustomerInvoiceSettings DROP CONSTRAINT CK_CIS_Provider;
EXEC sys.sp_executesql N'ALTER TABLE dbo.CustomerInvoiceSettings WITH CHECK ADD CONSTRAINT CK_CIS_Provider CHECK (Provider IN (''misa'',''s-invoice''))';
IF COL_LENGTH(N'dbo.CustomerInvoiceSettings', N'ProviderSecret') IS NOT NULL
    ALTER TABLE dbo.CustomerInvoiceSettings DROP COLUMN ProviderSecret;
COMMIT;
PRINT N'124-rollback-einvoice-softdreams-provider.sql: OK';
