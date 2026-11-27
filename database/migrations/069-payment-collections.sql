-- TINGEE-INTEGRATION - Payment collections ("Thu gộp công nợ"; operator 2026-11-27: put several unpaid orders of one customer
-- into one QR, the customer may also pay only part of it). Change TINGEE-INTEGRATION-CHG-20261127T130319Z-E75A07.
--   dbo.PaymentCollections        one payment request (TT-yyMMdd-NNNN) with its own QR and public page, for an amount the
--                                 seller chose (by default the sum still due on its orders).
--   dbo.PaymentCollectionOrders   the orders of a collection, in the order the money pays them (oldest first by default).
--   dbo.PaymentAllocations        how one bank payment to a collection was split over its orders.
--   dbo.PaymentTransactions.CollectionId  the collection a payment was applied to (OrderId stays NULL: the split is in
--                                 dbo.PaymentAllocations).
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, after 068.
-- Adds three tables and one nullable column; no row is changed. Idempotent.
-- Verify: database/scripts/125-verify-payment-collections.sql. Rollback: database/scripts/126-rollback-payment-collections.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
-- Equivalent required positive guard: DB_NAME() = 'TAPortalSys'. No USE statement.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.CustomerOrders', N'U') IS NULL OR OBJECT_ID(N'dbo.PaymentTransactions', N'U') IS NULL
   OR OBJECT_ID(N'dbo.CustomerBuyers', N'U') IS NULL OR OBJECT_ID(N'dbo.CustomerBankAccounts', N'U') IS NULL
 THROW 51001, 'Precondition failed: dbo.CustomerOrders, PaymentTransactions, CustomerBuyers or CustomerBankAccounts missing (011, 012, 019)', 1;

IF OBJECT_ID(N'dbo.PaymentCollections', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PaymentCollections (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_PaymentCollections PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        CustomerId uniqueidentifier NOT NULL CONSTRAINT FK_PC_Customer REFERENCES dbo.Customers(Id),
        CollectionNo varchar(30) NOT NULL,
        ContactId uniqueidentifier NULL CONSTRAINT FK_PC_Contact REFERENCES dbo.CustomerBuyers(Id),
        BankAccountId uniqueidentifier NULL CONSTRAINT FK_PC_BankAccount REFERENCES dbo.CustomerBankAccounts(Id),
        Description nvarchar(500) NULL,
        Amount decimal(18,0) NOT NULL,
        PaidAmount decimal(18,0) NOT NULL CONSTRAINT DF_PC_Paid DEFAULT 0,
        Status varchar(20) NOT NULL,
        QrType varchar(40) NULL,
        QrPayload nvarchar(2000) NULL,
        QrAccount varchar(50) NULL,
        BillId varchar(64) NULL,
        QrExpireMinutes int NOT NULL,
        ExpiresAt datetime2(3) NULL,
        PublicToken varchar(40) NULL,
        PartnerPaidAmount decimal(18,0) NULL,
        PartnerCheckedAt datetime2(3) NULL,
        LastError nvarchar(500) NULL,
        PaidAt datetime2(3) NULL,
        CancelledAt datetime2(3) NULL,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_PC_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        UpdatedAt datetime2(3) NULL,
        UpdatedBy uniqueidentifier NULL,
        CONSTRAINT CK_PC_Status CHECK (Status IN ('AWAITING_PAYMENT','PARTIALLY_PAID','PAID','CANCELLED')),
        CONSTRAINT CK_PC_Amount CHECK (Amount > 0 AND PaidAmount >= 0),
        CONSTRAINT CK_PC_Expire CHECK (QrExpireMinutes BETWEEN 1 AND 43200)
    );
    CREATE UNIQUE INDEX UX_PC_Customer_No ON dbo.PaymentCollections(CustomerId, CollectionNo);
    CREATE INDEX IX_PC_Customer_Status ON dbo.PaymentCollections(CustomerId, Status, CreatedAt DESC);
    CREATE UNIQUE INDEX UX_PC_BillId ON dbo.PaymentCollections(BillId) WHERE BillId IS NOT NULL;
    CREATE UNIQUE INDEX UX_PC_PublicToken ON dbo.PaymentCollections(PublicToken) WHERE PublicToken IS NOT NULL;
    PRINT N'dbo.PaymentCollections created';
END ELSE PRINT N'dbo.PaymentCollections exists';

IF OBJECT_ID(N'dbo.PaymentCollectionOrders', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PaymentCollectionOrders (
        CollectionId uniqueidentifier NOT NULL CONSTRAINT FK_PCO_Collection REFERENCES dbo.PaymentCollections(Id),
        OrderId uniqueidentifier NOT NULL CONSTRAINT FK_PCO_Order REFERENCES dbo.CustomerOrders(Id),
        Position int NOT NULL,
        DueAtCreate decimal(18,0) NOT NULL,
        CONSTRAINT PK_PaymentCollectionOrders PRIMARY KEY (CollectionId, OrderId),
        CONSTRAINT CK_PCO_Position CHECK (Position >= 1),
        CONSTRAINT CK_PCO_Due CHECK (DueAtCreate > 0)
    );
    CREATE INDEX IX_PCO_Order ON dbo.PaymentCollectionOrders(OrderId);
    PRINT N'dbo.PaymentCollectionOrders created';
END ELSE PRINT N'dbo.PaymentCollectionOrders exists';

IF OBJECT_ID(N'dbo.PaymentAllocations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PaymentAllocations (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_PaymentAllocations PRIMARY KEY,
        TransactionId bigint NOT NULL CONSTRAINT FK_PA_Transaction REFERENCES dbo.PaymentTransactions(Id),
        CollectionId uniqueidentifier NOT NULL CONSTRAINT FK_PA_Collection REFERENCES dbo.PaymentCollections(Id),
        OrderId uniqueidentifier NOT NULL CONSTRAINT FK_PA_Order REFERENCES dbo.CustomerOrders(Id),
        Amount decimal(18,0) NOT NULL,
        PaidBefore decimal(18,0) NOT NULL,
        PaidAfter decimal(18,0) NOT NULL,
        OrderStatusAfter varchar(20) NOT NULL,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_PA_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_PA_Amount CHECK (Amount > 0 AND PaidAfter = PaidBefore + Amount)
    );
    CREATE INDEX IX_PA_Transaction ON dbo.PaymentAllocations(TransactionId);
    CREATE INDEX IX_PA_Order ON dbo.PaymentAllocations(OrderId);
    CREATE INDEX IX_PA_Collection ON dbo.PaymentAllocations(CollectionId);
    PRINT N'dbo.PaymentAllocations created';
END ELSE PRINT N'dbo.PaymentAllocations exists';

IF COL_LENGTH(N'dbo.PaymentTransactions', N'CollectionId') IS NULL
BEGIN
    ALTER TABLE dbo.PaymentTransactions ADD CollectionId uniqueidentifier NULL CONSTRAINT FK_PT_Collection REFERENCES dbo.PaymentCollections(Id);
    PRINT N'dbo.PaymentTransactions.CollectionId added';
END ELSE PRINT N'dbo.PaymentTransactions.CollectionId exists';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.PaymentTransactions') AND name = N'IX_PT_Collection')
    EXEC sys.sp_executesql N'CREATE INDEX IX_PT_Collection ON dbo.PaymentTransactions(CollectionId) WHERE CollectionId IS NOT NULL;';

COMMIT;
PRINT N'069-payment-collections.sql: OK';
