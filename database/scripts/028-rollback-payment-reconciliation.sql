-- TINGEE-INTEGRATION — ROLLBACK of migrations/019-payment-reconciliation.sql.
-- Drops dbo.PaymentTransactions and the partner columns, and restores the original CHECK constraints. Refuses when rows
-- already use the new states or transactions exist (payments were recorded): export and decide with the operator first.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF EXISTS (SELECT 1 FROM dbo.CustomerOrders WHERE Status IN ('EXPIRED_PAID','CANCELLED_PAID'))
    THROW 51002, 'Rollback refused: orders in EXPIRED_PAID/CANCELLED_PAID exist; resolve them first', 1;
IF EXISTS (SELECT 1 FROM dbo.TingeeWebhookInbox WHERE Status = 'UNMATCHED')
    THROW 51003, 'Rollback refused: UNMATCHED webhooks exist; resolve them first', 1;
IF OBJECT_ID(N'dbo.PaymentTransactions', N'U') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.PaymentTransactions)
        THROW 51004, 'Rollback refused: payment transactions were recorded; export them first', 1;
    DROP TABLE dbo.PaymentTransactions;
END
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_TWI_Status' AND definition LIKE N'%UNMATCHED%')
BEGIN
    ALTER TABLE dbo.TingeeWebhookInbox DROP CONSTRAINT CK_TWI_Status;
    ALTER TABLE dbo.TingeeWebhookInbox WITH CHECK ADD CONSTRAINT CK_TWI_Status CHECK (Status IN ('RECEIVED','PROCESSING','PROCESSED','FAILED','IGNORED'));
END
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_CO_Status' AND definition LIKE N'%EXPIRED_PAID%')
BEGIN
    ALTER TABLE dbo.CustomerOrders DROP CONSTRAINT CK_CO_Status;
    ALTER TABLE dbo.CustomerOrders WITH CHECK ADD CONSTRAINT CK_CO_Status CHECK (Status IN ('DRAFT','AWAITING_PAYMENT','PARTIALLY_PAID','PAID','EXPIRED','CANCELLED'));
END
IF COL_LENGTH(N'dbo.CustomerOrders', N'PartnerPaidAmount') IS NOT NULL
    ALTER TABLE dbo.CustomerOrders DROP COLUMN PartnerPaidAmount, PartnerCheckedAt;
COMMIT;
PRINT N'028-rollback-payment-reconciliation.sql: OK';
