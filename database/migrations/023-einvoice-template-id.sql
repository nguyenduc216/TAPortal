-- CUSTOMER-PORTAL-FOUNDATION — MISA template id (operator report 2026-11-06). MIGRATION_NOT_EXECUTED.
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, after 022.
-- MISA meInvoice identifies a template by a GUID (36 characters, e.g. from invoice-templates), not by "1": the draft/create
-- call needs that value as invoicePattern. dbo.CustomerInvoiceSettings.InvoicePattern varchar(20) -> varchar(50).
-- Widening a nullable column only; no row is changed. Idempotent.
-- Verify: database/scripts/035-verify-einvoice-template-id.sql. Rollback: database/scripts/036-rollback-einvoice-template-id.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
-- Equivalent required positive guard: DB_NAME() = 'TAPortalSys'. No USE statement.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH(N'dbo.CustomerInvoiceSettings', N'InvoicePattern') IS NULL
 THROW 51001, 'Precondition failed: dbo.CustomerInvoiceSettings.InvoicePattern missing (run 022 first)', 1;
IF COL_LENGTH(N'dbo.CustomerInvoiceSettings', N'InvoicePattern') < 50
    ALTER TABLE dbo.CustomerInvoiceSettings ALTER COLUMN InvoicePattern varchar(50) NULL;
COMMIT;
PRINT N'023-einvoice-template-id.sql: OK';
