-- TINGEE-INTEGRATION — payment reconciliation (operator decisions 2026-11-05). MIGRATION_NOT_EXECUTED.
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys.
-- 1. dbo.CustomerOrders: two new states for money that arrives on an order that cannot simply take it —
--    EXPIRED_PAID (the QR had expired) and CANCELLED_PAID (the order was cancelled); PartnerPaidAmount/PartnerCheckedAt
--    keep the amount paid reported by the payment partner, next to the amount the system counted itself (PaidAmount).
-- 2. dbo.TingeeWebhookInbox: Status may also be UNMATCHED.
-- 3. dbo.PaymentTransactions: one row per payment webhook (every balance change the bank reports), with the order it
--    was applied to or why it was not (UNMATCHED, for staff), the amounts counted by the system and by the partner, and
--    how staff resolved it. NotifiedAt is used by the notification sender.
-- Adds columns/tables and widens two CHECK constraints; no row is changed or deleted. Idempotent.
-- Verify: database/scripts/027-verify-payment-reconciliation.sql. Rollback: database/scripts/028-rollback-payment-reconciliation.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
-- Equivalent required positive guard: DB_NAME() = 'TAPortalSys'. No USE statement.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.CustomerOrders', N'U') IS NULL OR OBJECT_ID(N'dbo.TingeeWebhookInbox', N'U') IS NULL OR OBJECT_ID(N'dbo.Customers', N'U') IS NULL
 THROW 51001, 'Precondition failed: dbo.CustomerOrders, dbo.TingeeWebhookInbox or dbo.Customers missing (migration 011)', 1;

-- 1. Orders
IF COL_LENGTH(N'dbo.CustomerOrders', N'PartnerPaidAmount') IS NULL
BEGIN
    ALTER TABLE dbo.CustomerOrders ADD PartnerPaidAmount decimal(18,0) NULL, PartnerCheckedAt datetime2(3) NULL;
    PRINT N'dbo.CustomerOrders.PartnerPaidAmount/PartnerCheckedAt added';
END ELSE PRINT N'dbo.CustomerOrders.PartnerPaidAmount exists';

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.CustomerOrders') AND name = N'CK_CO_Status'
               AND definition LIKE N'%EXPIRED_PAID%')
BEGIN
    IF OBJECT_ID(N'dbo.CK_CO_Status', N'C') IS NOT NULL ALTER TABLE dbo.CustomerOrders DROP CONSTRAINT CK_CO_Status;
    ALTER TABLE dbo.CustomerOrders WITH CHECK ADD CONSTRAINT CK_CO_Status
        CHECK (Status IN ('DRAFT','AWAITING_PAYMENT','PARTIALLY_PAID','PAID','EXPIRED','CANCELLED','EXPIRED_PAID','CANCELLED_PAID'));
    PRINT N'CK_CO_Status widened (EXPIRED_PAID, CANCELLED_PAID)';
END ELSE PRINT N'CK_CO_Status already widened';

-- 2. Inbox
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.TingeeWebhookInbox') AND name = N'CK_TWI_Status'
               AND definition LIKE N'%UNMATCHED%')
BEGIN
    IF OBJECT_ID(N'dbo.CK_TWI_Status', N'C') IS NOT NULL ALTER TABLE dbo.TingeeWebhookInbox DROP CONSTRAINT CK_TWI_Status;
    ALTER TABLE dbo.TingeeWebhookInbox WITH CHECK ADD CONSTRAINT CK_TWI_Status
        CHECK (Status IN ('RECEIVED','PROCESSING','PROCESSED','FAILED','IGNORED','UNMATCHED'));
    PRINT N'CK_TWI_Status widened (UNMATCHED)';
END ELSE PRINT N'CK_TWI_Status already widened';

-- 3. Transactions
IF OBJECT_ID(N'dbo.PaymentTransactions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PaymentTransactions (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_PaymentTransactions PRIMARY KEY,
        InboxId bigint NULL CONSTRAINT FK_PT_Inbox REFERENCES dbo.TingeeWebhookInbox(Id),
        Direction varchar(10) NOT NULL,
        TransactionCode varchar(100) NULL,
        Amount decimal(18,0) NOT NULL,
        Content nvarchar(500) NULL,
        Bank varchar(30) NULL,
        AccountNumber varchar(50) NULL,
        VaAccountNumber varchar(50) NULL,
        TransactionAt datetime2(3) NULL,
        ReceivedAt datetime2(3) NOT NULL,
        CustomerId uniqueidentifier NULL CONSTRAINT FK_PT_Customer REFERENCES dbo.Customers(Id),
        OrderId uniqueidentifier NULL CONSTRAINT FK_PT_Order REFERENCES dbo.CustomerOrders(Id),
        Status varchar(20) NOT NULL,
        Flags varchar(100) NULL,
        Reason nvarchar(500) NULL,
        MatchedBy varchar(20) NULL,
        SystemPaidBefore decimal(18,0) NULL,
        SystemPaidAfter decimal(18,0) NULL,
        PartnerPaidTotal decimal(18,0) NULL,
        OrderStatusAfter varchar(20) NULL,
        ResolvedAt datetime2(3) NULL,
        ResolvedBy uniqueidentifier NULL,
        ResolvedVia varchar(10) NULL,
        ResolutionNote nvarchar(500) NULL,
        NotifiedAt datetime2(3) NULL,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_PT_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt datetime2(3) NULL,
        CONSTRAINT CK_PT_Direction CHECK (Direction IN ('CREDIT','DEBIT')),
        CONSTRAINT CK_PT_Status CHECK (Status IN ('MATCHED','REVIEW','UNMATCHED','RESOLVED','IGNORED')),
        CONSTRAINT CK_PT_Via CHECK (ResolvedVia IS NULL OR ResolvedVia IN ('ADMIN','PORTAL')),
        CONSTRAINT CK_PT_Amount CHECK (Amount >= 0)
    );
    CREATE UNIQUE INDEX UX_PT_Inbox ON dbo.PaymentTransactions(InboxId) WHERE InboxId IS NOT NULL;
    CREATE INDEX IX_PT_Status ON dbo.PaymentTransactions(Status, ReceivedAt DESC);
    CREATE INDEX IX_PT_Customer ON dbo.PaymentTransactions(CustomerId, ReceivedAt DESC);
    CREATE INDEX IX_PT_Order ON dbo.PaymentTransactions(OrderId) WHERE OrderId IS NOT NULL;
    PRINT N'dbo.PaymentTransactions created';
END ELSE PRINT N'dbo.PaymentTransactions exists';

COMMIT;
PRINT N'019-payment-reconciliation.sql: OK';
