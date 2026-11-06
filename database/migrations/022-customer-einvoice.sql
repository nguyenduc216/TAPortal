-- CUSTOMER-PORTAL-FOUNDATION — automatic e-invoice for paid QR orders, MISA meInvoice (operator request 2026-11-06). MIGRATION_NOT_EXECUTED.
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, after 021.
-- 1. dbo.CustomerInvoiceSettings: one row per company — its MISA account at the e-invoice partner (account id, tax code,
--    username; the password goes to the partner only and is never stored here), the template (mẫu số + ký hiệu), whether
--    paid orders are invoiced automatically, from when (AutoIssueFrom: orders paid before auto-issue was turned on are never
--    invoiced automatically) and whether the invoice is emailed to the buyer.
-- 2. dbo.CustomerOrders: invoice result columns. InvoiceCode is fixed on the first attempt so a retry cannot issue twice.
--    InvoiceAttempts / InvoiceNextAttemptAt / InvoiceLockedUntil drive the webhook site's worker.
-- New table + nullable/defaulted columns only; no existing row is changed. Idempotent.
-- Verify: database/scripts/033-verify-customer-einvoice.sql. Rollback: database/scripts/034-rollback-customer-einvoice.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
-- Equivalent required positive guard: DB_NAME() = 'TAPortalSys'. No USE statement.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.CustomerOrders', N'U') IS NULL OR OBJECT_ID(N'dbo.Customers', N'U') IS NULL
 THROW 51001, 'Precondition failed: dbo.CustomerOrders / dbo.Customers missing (run 011 first)', 1;

IF OBJECT_ID(N'dbo.CustomerInvoiceSettings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustomerInvoiceSettings (
        CustomerId uniqueidentifier NOT NULL CONSTRAINT PK_CustomerInvoiceSettings PRIMARY KEY,
        Provider varchar(20) NOT NULL CONSTRAINT DF_CIS_Provider DEFAULT 'misa',
        ProviderAccountId bigint NULL,
        ProviderTaxCode varchar(20) NULL,
        ProviderUsername nvarchar(150) NULL,
        InvoicePattern varchar(20) NULL,
        InvoiceSeries varchar(20) NULL,
        AutoIssueEnabled bit NOT NULL CONSTRAINT DF_CIS_Auto DEFAULT 0,
        SendEmail bit NOT NULL CONSTRAINT DF_CIS_Email DEFAULT 1,
        AutoIssueFrom datetime2(3) NULL,
        ConnectedAt datetime2(3) NULL,
        UpdatedAt datetime2(3) NOT NULL CONSTRAINT DF_CIS_UpdatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedBy uniqueidentifier NULL,
        CONSTRAINT FK_CIS_Customer FOREIGN KEY (CustomerId) REFERENCES dbo.Customers(Id),
        CONSTRAINT CK_CIS_Provider CHECK (Provider IN ('misa'))
    );
    PRINT N'dbo.CustomerInvoiceSettings created';
END ELSE PRINT N'dbo.CustomerInvoiceSettings exists';

IF COL_LENGTH(N'dbo.CustomerOrders', N'InvoiceCode') IS NULL
BEGIN
    ALTER TABLE dbo.CustomerOrders ADD
        InvoiceCode varchar(40) NULL,
        InvoiceNumber varchar(40) NULL,
        InvoiceLookupCode varchar(60) NULL,
        InvoiceTrackingCode varchar(100) NULL,
        InvoiceIssuedAt datetime2(3) NULL,
        InvoiceEmailedAt datetime2(3) NULL,
        InvoiceError nvarchar(500) NULL,
        InvoiceAttempts int NOT NULL CONSTRAINT DF_CO_InvoiceAttempts DEFAULT 0,
        InvoiceNextAttemptAt datetime2(3) NULL,
        InvoiceLockedUntil datetime2(3) NULL;
    PRINT N'dbo.CustomerOrders: 10 invoice columns added';
END ELSE PRINT N'dbo.CustomerOrders: invoice columns exist';

-- Dynamic SQL: the columns above are unknown when this batch is compiled.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.CustomerOrders') AND name = N'UX_CO_InvoiceCode')
    EXEC sys.sp_executesql N'CREATE UNIQUE INDEX UX_CO_InvoiceCode ON dbo.CustomerOrders(InvoiceCode) WHERE InvoiceCode IS NOT NULL';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.CustomerOrders') AND name = N'IX_CO_InvoiceQueue')
    EXEC sys.sp_executesql N'CREATE INDEX IX_CO_InvoiceQueue ON dbo.CustomerOrders(Status, InvoiceStatus) INCLUDE (CustomerId, AutoIssueInvoice, PaidAt, InvoiceNextAttemptAt, InvoiceLockedUntil)';

COMMIT;
PRINT N'022-customer-einvoice.sql: OK';
