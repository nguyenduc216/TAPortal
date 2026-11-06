-- CUSTOMER-PORTAL-FOUNDATION — draft invoice status (operator request 2026-11-07). MIGRATION_NOT_EXECUTED.
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, after 025.
-- When MISA cannot sign an order's invoice (400 "CallSignServiceFail": no certificate usable for API issuing), the
-- applications create a draft with the order's invoice code instead and mark the order DRAFT rather than FAILED.
--   dbo.CustomerOrders  CK_CO_InvoiceStatus  NONE, PENDING, ISSUED, FAILED  ->  + DRAFT
-- Widens a CHECK constraint only; no row is changed. Idempotent.
-- Verify: database/scripts/041-verify-order-invoice-draft.sql. Rollback: database/scripts/042-rollback-order-invoice-draft.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
-- Equivalent required positive guard: DB_NAME() = 'TAPortalSys'. No USE statement.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH(N'dbo.CustomerOrders', N'InvoiceStatus') IS NULL
 THROW 51001, 'Precondition failed: dbo.CustomerOrders.InvoiceStatus missing (run 011 first)', 1;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_CO_InvoiceStatus' AND parent_object_id = OBJECT_ID(N'dbo.CustomerOrders')
               AND definition LIKE N'%DRAFT%')
BEGIN
    IF OBJECT_ID(N'dbo.CK_CO_InvoiceStatus', N'C') IS NOT NULL
        ALTER TABLE dbo.CustomerOrders DROP CONSTRAINT CK_CO_InvoiceStatus;
    ALTER TABLE dbo.CustomerOrders WITH CHECK ADD CONSTRAINT CK_CO_InvoiceStatus
        CHECK (InvoiceStatus IN ('NONE','PENDING','ISSUED','FAILED','DRAFT'));
END
COMMIT;
PRINT N'026-order-invoice-draft.sql: OK';
