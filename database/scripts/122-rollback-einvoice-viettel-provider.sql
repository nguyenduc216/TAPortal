-- TINGEE-INTEGRATION - ROLLBACK of migrations/067-einvoice-viettel-provider.sql.
-- Restores CK_CIS_Provider to Provider IN ('misa'). Refuses once a company has connected Viettel SInvoice (that row would
-- violate the constraint and must not be changed here): decide with the operator first.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.CustomerInvoiceSettings', N'U') IS NULL
 THROW 51001, 'Precondition failed: dbo.CustomerInvoiceSettings missing', 1;
IF EXISTS (SELECT 1 FROM dbo.CustomerInvoiceSettings WHERE Provider <> 'misa')
    THROW 51002, 'Rollback refused: companies use an e-invoice provider other than misa', 1;
IF OBJECT_ID(N'dbo.CK_CIS_Provider', N'C') IS NOT NULL ALTER TABLE dbo.CustomerInvoiceSettings DROP CONSTRAINT CK_CIS_Provider;
EXEC sys.sp_executesql N'ALTER TABLE dbo.CustomerInvoiceSettings WITH CHECK ADD CONSTRAINT CK_CIS_Provider CHECK (Provider IN (''misa''))';
COMMIT;
PRINT N'122-rollback-einvoice-viettel-provider.sql: OK';
