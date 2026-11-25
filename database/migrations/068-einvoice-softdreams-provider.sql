-- TINGEE-INTEGRATION - E-invoice provider SoftDreams EasyInvoice, integrated directly (operator 2026-11-26: "thêm nhà phân phối
-- hóa đơn SoftDreams, tích hợp API"; login kept encrypted: "Mã hoá lưu trong DB").
-- Change TINGEE-INTEGRATION-CHG-20261125T234234Z-EAFC57.
--   dbo.CustomerInvoiceSettings.CK_CIS_Provider: Provider IN ('misa','s-invoice') -> IN ('misa','s-invoice','softdreams').
--   dbo.CustomerInvoiceSettings.ProviderSecret VARBINARY(MAX) NULL: the EasyInvoice password, DPAPI-encrypted by the application
--   (machine scope); NULL for MISA/Viettel, whose password is never stored.
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, after 067.
-- Adds one nullable column and widens one CHECK constraint; no row is changed. Idempotent.
-- Verify: database/scripts/123-verify-einvoice-softdreams-provider.sql. Rollback: database/scripts/124-rollback-einvoice-softdreams-provider.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
-- Equivalent required positive guard: DB_NAME() = 'TAPortalSys'. No USE statement.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.CustomerInvoiceSettings', N'U') IS NULL
 THROW 51001, 'Precondition failed: dbo.CustomerInvoiceSettings missing (run 022 first)', 1;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_CIS_Provider' AND parent_object_id = OBJECT_ID(N'dbo.CustomerInvoiceSettings')
               AND definition LIKE N'%s-invoice%')
 THROW 51002, 'Precondition failed: CK_CIS_Provider does not allow s-invoice (run 067 first)', 1;
IF COL_LENGTH(N'dbo.CustomerInvoiceSettings', N'ProviderSecret') IS NULL
    ALTER TABLE dbo.CustomerInvoiceSettings ADD ProviderSecret varbinary(max) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_CIS_Provider' AND parent_object_id = OBJECT_ID(N'dbo.CustomerInvoiceSettings')
               AND definition LIKE N'%softdreams%')
BEGIN
    IF OBJECT_ID(N'dbo.CK_CIS_Provider', N'C') IS NOT NULL ALTER TABLE dbo.CustomerInvoiceSettings DROP CONSTRAINT CK_CIS_Provider;
    EXEC sys.sp_executesql N'ALTER TABLE dbo.CustomerInvoiceSettings WITH CHECK ADD CONSTRAINT CK_CIS_Provider CHECK (Provider IN (''misa'',''s-invoice'',''softdreams''))';
END
COMMIT;
PRINT N'068-einvoice-softdreams-provider.sql: OK';
