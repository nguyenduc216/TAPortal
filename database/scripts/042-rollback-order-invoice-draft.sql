-- CUSTOMER-PORTAL-FOUNDATION — ROLLBACK of migrations/026-order-invoice-draft.sql.
-- Restores CK_CO_InvoiceStatus to NONE, PENDING, ISSUED, FAILED. Refuses while an order is DRAFT (its draft exists at
-- the provider): decide with the operator first.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF EXISTS (SELECT 1 FROM dbo.CustomerOrders WHERE InvoiceStatus = 'DRAFT')
    THROW 51002, 'Rollback refused: orders with InvoiceStatus DRAFT exist', 1;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_CO_InvoiceStatus' AND parent_object_id = OBJECT_ID(N'dbo.CustomerOrders')
           AND definition LIKE N'%DRAFT%')
BEGIN
    ALTER TABLE dbo.CustomerOrders DROP CONSTRAINT CK_CO_InvoiceStatus;
    ALTER TABLE dbo.CustomerOrders WITH CHECK ADD CONSTRAINT CK_CO_InvoiceStatus
        CHECK (InvoiceStatus IN ('NONE','PENDING','ISSUED','FAILED'));
END
COMMIT;
PRINT N'042-rollback-order-invoice-draft.sql: OK';
