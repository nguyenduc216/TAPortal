-- TINGEE-INTEGRATION - E-invoice provider Viettel SInvoice beside MISA meInvoice (operator 2026-11-23: "ok thêm vào hệ thống").
-- Change TINGEE-INTEGRATION-CHG-20261123T090237Z-0F6D3B.
--   dbo.CustomerInvoiceSettings.CK_CIS_Provider: Provider IN ('misa') -> Provider IN ('misa','s-invoice') (the partner's code of SInvoice).
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, after 066.
-- Widens one CHECK constraint; no row is changed (every stored row is 'misa'). Idempotent.
-- Verify: database/scripts/121-verify-einvoice-viettel-provider.sql. Rollback: database/scripts/122-rollback-einvoice-viettel-provider.sql.
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
BEGIN
    IF OBJECT_ID(N'dbo.CK_CIS_Provider', N'C') IS NOT NULL ALTER TABLE dbo.CustomerInvoiceSettings DROP CONSTRAINT CK_CIS_Provider;
    EXEC sys.sp_executesql N'ALTER TABLE dbo.CustomerInvoiceSettings WITH CHECK ADD CONSTRAINT CK_CIS_Provider CHECK (Provider IN (''misa'',''s-invoice''))';
END
COMMIT;
PRINT N'067-einvoice-viettel-provider.sql: OK';
