-- TINGEE-INTEGRATION-CHG-20261029T203104Z-1FE09C — Tingee payments foundation. MIGRATION_NOT_EXECUTED.
-- Manual execution only (SSMS/DBeaver) on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, after a backup.
-- Creates (only when missing) the tables used by Admin (Tingee merchant mapping), Customer Portal (bank accounts,
-- contacts, orders with QR, payment settings) and TAPortal.Webhooks (webhook inbox). Never alters or drops existing
-- objects and writes no data. Idempotent: re-running prints "exists" for each table.
-- Order: run after 008-customer-portal-memberships.sql. Verify: database/scripts/012-verify-tingee-foundation.sql.
-- Rollback (only while the new tables are empty, after approval): database/scripts/013-rollback-tingee-foundation.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
-- Equivalent required positive guard: DB_NAME() = 'TAPortalSys'. No USE statement.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.Customers','U') IS NULL OR OBJECT_ID('dbo.CustomerPortalMemberships','U') IS NULL
 THROW 51000, 'Precondition failed: dbo.Customers / dbo.CustomerPortalMemberships missing (run 007/008 first)', 1;

-- 1. Customer (T.A) <-> Tingee child merchant. One merchant per customer and one customer per merchant.
IF OBJECT_ID(N'dbo.TingeeMerchants', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TingeeMerchants (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_TingeeMerchants PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        CustomerId uniqueidentifier NOT NULL,
        MerchantId bigint NOT NULL,
        MerchantCode varchar(20) NULL,
        PhoneNumber varchar(20) NULL,
        Email nvarchar(256) NULL,
        Source varchar(20) NOT NULL,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_TingeeMerchants_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        CONSTRAINT FK_TingeeMerchants_Customer FOREIGN KEY (CustomerId) REFERENCES dbo.Customers(Id),
        CONSTRAINT UQ_TingeeMerchants_Customer UNIQUE (CustomerId),
        CONSTRAINT UQ_TingeeMerchants_Merchant UNIQUE (MerchantId),
        CONSTRAINT CK_TingeeMerchants_Source CHECK (Source IN ('CREATED','LINKED'))
    );
    PRINT N'dbo.TingeeMerchants created';
END ELSE PRINT N'dbo.TingeeMerchants exists';

-- 2. Bank accounts linked through Tingee (VA or root account notification).
IF OBJECT_ID(N'dbo.CustomerBankAccounts', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustomerBankAccounts (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_CustomerBankAccounts PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        CustomerId uniqueidentifier NOT NULL,
        BankBin varchar(20) NOT NULL,
        BankCode varchar(20) NOT NULL,
        BankName nvarchar(100) NOT NULL,
        AccountType varchar(40) NOT NULL,
        AccountNumber varchar(50) NULL,
        AccountName nvarchar(200) NULL,
        VaAccountNumber varchar(50) NULL,
        IsNotifyAccountNumber bit NOT NULL CONSTRAINT DF_CBA_Notify DEFAULT 0,
        TingeeShopId bigint NULL,
        Status varchar(20) NOT NULL,
        ConfirmId varchar(100) NULL,
        ConfirmExpiresAt datetime2(3) NULL,
        ActionLink nvarchar(1000) NULL,
        LastError nvarchar(500) NULL,
        IsDefault bit NOT NULL CONSTRAINT DF_CBA_Default DEFAULT 0,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_CBA_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        UpdatedAt datetime2(3) NULL,
        UpdatedBy uniqueidentifier NULL,
        CONSTRAINT FK_CBA_Customer FOREIGN KEY (CustomerId) REFERENCES dbo.Customers(Id),
        CONSTRAINT CK_CBA_Status CHECK (Status IN ('PENDING_OTP','PENDING_BANK','ACTIVE','FAILED','PENDING_DELETE','DELETED')),
        CONSTRAINT CK_CBA_AccountType CHECK (AccountType IN ('personal-account','business-household-account','business-account'))
    );
    CREATE INDEX IX_CBA_Customer_Status ON dbo.CustomerBankAccounts(CustomerId, Status);
    CREATE UNIQUE INDEX UX_CBA_Customer_Va ON dbo.CustomerBankAccounts(CustomerId, BankBin, VaAccountNumber)
        WHERE VaAccountNumber IS NOT NULL AND Status <> 'DELETED';
    PRINT N'dbo.CustomerBankAccounts created';
END ELSE PRINT N'dbo.CustomerBankAccounts exists';

-- 3. The customer's own customers (payers / invoice buyers).
IF OBJECT_ID(N'dbo.CustomerContacts', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustomerContacts (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_CustomerContacts PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        CustomerId uniqueidentifier NOT NULL,
        Code varchar(50) NOT NULL,
        Name nvarchar(250) NOT NULL,
        LegalName nvarchar(250) NULL,
        TaxCode varchar(20) NULL,
        Phone varchar(20) NULL,
        Email nvarchar(256) NULL,
        Address nvarchar(500) NULL,
        Note nvarchar(1000) NULL,
        IsActive bit NOT NULL CONSTRAINT DF_CC_Active DEFAULT 1,
        IsDeleted bit NOT NULL CONSTRAINT DF_CC_Deleted DEFAULT 0,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_CC_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        UpdatedAt datetime2(3) NULL,
        UpdatedBy uniqueidentifier NULL,
        CONSTRAINT FK_CC_Customer FOREIGN KEY (CustomerId) REFERENCES dbo.Customers(Id)
    );
    CREATE UNIQUE INDEX UX_CC_Customer_Code ON dbo.CustomerContacts(CustomerId, Code) WHERE IsDeleted = 0;
    CREATE INDEX IX_CC_Customer_Name ON dbo.CustomerContacts(CustomerId, IsDeleted, Name);
    PRINT N'dbo.CustomerContacts created';
END ELSE PRINT N'dbo.CustomerContacts exists';

-- 4. Orders paid by QR (dynamic recurring QR until fully paid, or static VietQR with the order number as content).
IF OBJECT_ID(N'dbo.CustomerOrders', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustomerOrders (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_CustomerOrders PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        CustomerId uniqueidentifier NOT NULL,
        OrderNo varchar(30) NOT NULL,
        ContactId uniqueidentifier NULL,
        BankAccountId uniqueidentifier NULL,
        Description nvarchar(500) NULL,
        Amount decimal(18,0) NOT NULL,
        PaidAmount decimal(18,0) NOT NULL CONSTRAINT DF_CO_Paid DEFAULT 0,
        Status varchar(20) NOT NULL,
        QrType varchar(40) NULL,
        QrPayload nvarchar(2000) NULL,
        QrAccount varchar(50) NULL,
        BillId varchar(64) NULL,
        QrExpireMinutes int NOT NULL,
        ExpiresAt datetime2(3) NULL,
        AutoIssueInvoice bit NOT NULL,
        InvoiceStatus varchar(20) NOT NULL CONSTRAINT DF_CO_Invoice DEFAULT 'NONE',
        PaidAt datetime2(3) NULL,
        CancelledAt datetime2(3) NULL,
        LastError nvarchar(500) NULL,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_CO_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        UpdatedAt datetime2(3) NULL,
        UpdatedBy uniqueidentifier NULL,
        CONSTRAINT FK_CO_Customer FOREIGN KEY (CustomerId) REFERENCES dbo.Customers(Id),
        CONSTRAINT FK_CO_Contact FOREIGN KEY (ContactId) REFERENCES dbo.CustomerContacts(Id),
        CONSTRAINT FK_CO_BankAccount FOREIGN KEY (BankAccountId) REFERENCES dbo.CustomerBankAccounts(Id),
        CONSTRAINT CK_CO_Status CHECK (Status IN ('DRAFT','AWAITING_PAYMENT','PARTIALLY_PAID','PAID','EXPIRED','CANCELLED')),
        CONSTRAINT CK_CO_InvoiceStatus CHECK (InvoiceStatus IN ('NONE','PENDING','ISSUED','FAILED')),
        CONSTRAINT CK_CO_Amount CHECK (Amount > 0 AND PaidAmount >= 0),
        CONSTRAINT CK_CO_Expire CHECK (QrExpireMinutes BETWEEN 1 AND 43200)
    );
    CREATE UNIQUE INDEX UX_CO_Customer_OrderNo ON dbo.CustomerOrders(CustomerId, OrderNo);
    CREATE INDEX IX_CO_Customer_Status ON dbo.CustomerOrders(CustomerId, Status, CreatedAt DESC);
    CREATE UNIQUE INDEX UX_CO_BillId ON dbo.CustomerOrders(BillId) WHERE BillId IS NOT NULL;
    PRINT N'dbo.CustomerOrders created';
END ELSE PRINT N'dbo.CustomerOrders exists';

IF OBJECT_ID(N'dbo.CustomerOrderLines', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustomerOrderLines (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_CustomerOrderLines PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        OrderId uniqueidentifier NOT NULL,
        LineNumber int NOT NULL,
        ItemName nvarchar(500) NOT NULL,
        UnitName nvarchar(50) NULL,
        Quantity decimal(18,3) NOT NULL,
        UnitPrice decimal(18,0) NOT NULL,
        TaxRateCode varchar(3) NOT NULL,
        AmountBeforeTax decimal(18,0) NOT NULL,
        TaxAmount decimal(18,0) NOT NULL,
        CONSTRAINT FK_COL_Order FOREIGN KEY (OrderId) REFERENCES dbo.CustomerOrders(Id),
        CONSTRAINT UQ_COL_Order_Line UNIQUE (OrderId, LineNumber),
        CONSTRAINT CK_COL_TaxRate CHECK (TaxRateCode IN ('0','5','8','10','-1','-2')),
        CONSTRAINT CK_COL_Values CHECK (Quantity > 0 AND UnitPrice >= 0)
    );
    PRINT N'dbo.CustomerOrderLines created';
END ELSE PRINT N'dbo.CustomerOrderLines exists';

-- 5. Per-customer payment defaults (operator decision: auto e-invoice ON by default).
IF OBJECT_ID(N'dbo.CustomerPaymentSettings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustomerPaymentSettings (
        CustomerId uniqueidentifier NOT NULL CONSTRAINT PK_CustomerPaymentSettings PRIMARY KEY,
        AutoIssueInvoiceDefault bit NOT NULL CONSTRAINT DF_CPS_AutoInvoice DEFAULT 1,
        DefaultQrExpireMinutes int NOT NULL CONSTRAINT DF_CPS_Expire DEFAULT 1440,
        UpdatedAt datetime2(3) NOT NULL CONSTRAINT DF_CPS_UpdatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedBy uniqueidentifier NULL,
        CONSTRAINT FK_CPS_Customer FOREIGN KEY (CustomerId) REFERENCES dbo.Customers(Id),
        CONSTRAINT CK_CPS_Expire CHECK (DefaultQrExpireMinutes BETWEEN 1 AND 43200)
    );
    PRINT N'dbo.CustomerPaymentSettings created';
END ELSE PRINT N'dbo.CustomerPaymentSettings exists';

-- 6. Verified Tingee webhooks, stored verbatim before TAPortal.Webhooks acknowledges (idempotent by Kind + DedupKey).
IF OBJECT_ID(N'dbo.TingeeWebhookInbox', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TingeeWebhookInbox (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_TingeeWebhookInbox PRIMARY KEY,
        Kind varchar(20) NOT NULL,
        DedupKey varchar(150) NOT NULL,
        RequestId varchar(100) NULL,
        RequestTimestamp varchar(20) NOT NULL,
        RawBody nvarchar(max) NOT NULL,
        ReceivedAt datetime2(3) NOT NULL,
        Status varchar(20) NOT NULL CONSTRAINT DF_TWI_Status DEFAULT 'RECEIVED',
        Attempts int NOT NULL CONSTRAINT DF_TWI_Attempts DEFAULT 0,
        ProcessedAt datetime2(3) NULL,
        LastError nvarchar(1000) NULL,
        CONSTRAINT CK_TWI_Kind CHECK (Kind IN ('Payment','VaStatus','BankLink')),
        CONSTRAINT CK_TWI_Status CHECK (Status IN ('RECEIVED','PROCESSING','PROCESSED','FAILED','IGNORED'))
    );
    CREATE UNIQUE INDEX UX_TWI_Kind_Dedup ON dbo.TingeeWebhookInbox(Kind, DedupKey);
    CREATE INDEX IX_TWI_Status ON dbo.TingeeWebhookInbox(Status, Id);
    PRINT N'dbo.TingeeWebhookInbox created';
END ELSE PRINT N'dbo.TingeeWebhookInbox exists';

COMMIT;
PRINT N'011-tingee-payments-foundation.sql: OK';
