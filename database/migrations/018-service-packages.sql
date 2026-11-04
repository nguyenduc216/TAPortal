-- CUSTOMER-PORTAL-FOUNDATION-CHG-20261104T112825Z-F9FA74 — service packages and package purchases. MIGRATION_NOT_EXECUTED.
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys.
-- dbo.ServicePackages: what T.A sells — an invoice package, a transaction-reconciliation package, or a combo of both;
--   price includes VAT; ValidityDays NULL = the quantities never expire.
-- dbo.ServicePackagePurchases: one row per purchase. It snapshots the package at request time and, once paid, is a
--   balance lot: InvoicesRemaining / TransactionsRemaining are consumed later; ExpiresAt = PaidAt + ValidityDays.
--   A customer's available balance = sum of the remaining quantities of PAID lots that have not expired.
-- Creates new tables only. Idempotent. Verify: database/scripts/025-verify-service-packages.sql.
-- Rollback: database/scripts/026-rollback-service-packages.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
-- Equivalent required positive guard: DB_NAME() = 'TAPortalSys'. No USE statement.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.Customers', N'U') IS NULL OR OBJECT_ID(N'dbo.Users', N'U') IS NULL
 THROW 51001, 'Precondition failed: dbo.Customers or dbo.Users missing', 1;

IF OBJECT_ID(N'dbo.ServicePackages', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ServicePackages (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_ServicePackages PRIMARY KEY CONSTRAINT DF_SP_Id DEFAULT NEWID(),
        Code varchar(30) NOT NULL,
        Name nvarchar(200) NOT NULL,
        PackageType varchar(20) NOT NULL,
        InvoiceQuota int NOT NULL CONSTRAINT DF_SP_InvoiceQuota DEFAULT 0,
        TransactionQuota int NOT NULL CONSTRAINT DF_SP_TransactionQuota DEFAULT 0,
        Price decimal(18,0) NOT NULL,
        ValidityDays int NULL,
        Description nvarchar(1000) NULL,
        IsActive bit NOT NULL CONSTRAINT DF_SP_IsActive DEFAULT 1,
        SortOrder int NOT NULL CONSTRAINT DF_SP_SortOrder DEFAULT 0,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_SP_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        UpdatedAt datetime2(3) NULL,
        UpdatedBy uniqueidentifier NULL,
        CONSTRAINT CK_SP_Type CHECK (
            (PackageType = 'INVOICE' AND InvoiceQuota > 0 AND TransactionQuota = 0)
         OR (PackageType = 'TRANSACTION' AND TransactionQuota > 0 AND InvoiceQuota = 0)
         OR (PackageType = 'COMBO' AND InvoiceQuota > 0 AND TransactionQuota > 0)),
        CONSTRAINT CK_SP_Price CHECK (Price >= 0),
        CONSTRAINT CK_SP_Validity CHECK (ValidityDays IS NULL OR ValidityDays BETWEEN 1 AND 3650)
    );
    CREATE UNIQUE INDEX UX_ServicePackages_Code ON dbo.ServicePackages(Code);
    PRINT N'dbo.ServicePackages created';
END ELSE PRINT N'dbo.ServicePackages exists';

IF OBJECT_ID(N'dbo.ServicePackagePurchases', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ServicePackagePurchases (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_ServicePackagePurchases PRIMARY KEY CONSTRAINT DF_SPP_Id DEFAULT NEWID(),
        PurchaseNo varchar(30) NOT NULL,
        CustomerId uniqueidentifier NOT NULL CONSTRAINT FK_SPP_Customer REFERENCES dbo.Customers(Id),
        PackageId uniqueidentifier NOT NULL CONSTRAINT FK_SPP_Package REFERENCES dbo.ServicePackages(Id),
        PackageCode varchar(30) NOT NULL,
        PackageName nvarchar(200) NOT NULL,
        PackageType varchar(20) NOT NULL,
        InvoiceQuota int NOT NULL,
        TransactionQuota int NOT NULL,
        Amount decimal(18,0) NOT NULL,
        ValidityDays int NULL,
        Status varchar(20) NOT NULL CONSTRAINT DF_SPP_Status DEFAULT 'PENDING',
        RequestedVia varchar(20) NOT NULL,
        RequestedBy uniqueidentifier NULL,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_SPP_CreatedAt DEFAULT SYSUTCDATETIME(),
        PaidAmount decimal(18,0) NULL,
        PaidAt datetime2(3) NULL,
        PaymentMethod varchar(20) NULL,
        PaymentReference nvarchar(200) NULL,
        ConfirmedBy uniqueidentifier NULL,
        ExpiresAt datetime2(3) NULL,
        InvoicesRemaining int NOT NULL CONSTRAINT DF_SPP_InvoicesRemaining DEFAULT 0,
        TransactionsRemaining int NOT NULL CONSTRAINT DF_SPP_TransactionsRemaining DEFAULT 0,
        CancelledAt datetime2(3) NULL,
        CancelledBy uniqueidentifier NULL,
        CancelReason nvarchar(500) NULL,
        UpdatedAt datetime2(3) NULL,
        CONSTRAINT CK_SPP_Status CHECK (Status IN ('PENDING', 'PAID', 'CANCELLED')),
        CONSTRAINT CK_SPP_Via CHECK (RequestedVia IN ('CUSTOMER', 'ADMIN')),
        CONSTRAINT CK_SPP_Remaining CHECK (InvoicesRemaining >= 0 AND TransactionsRemaining >= 0
            AND InvoicesRemaining <= InvoiceQuota AND TransactionsRemaining <= TransactionQuota),
        CONSTRAINT CK_SPP_Paid CHECK (Status <> 'PAID' OR (PaidAt IS NOT NULL AND PaidAmount IS NOT NULL))
    );
    CREATE UNIQUE INDEX UX_SPP_PurchaseNo ON dbo.ServicePackagePurchases(PurchaseNo);
    CREATE INDEX IX_SPP_Customer_Status ON dbo.ServicePackagePurchases(CustomerId, Status, ExpiresAt);
    CREATE INDEX IX_SPP_Status_Created ON dbo.ServicePackagePurchases(Status, CreatedAt);
    PRINT N'dbo.ServicePackagePurchases created';
END ELSE PRINT N'dbo.ServicePackagePurchases exists';
COMMIT;
PRINT N'018-service-packages.sql: OK';
