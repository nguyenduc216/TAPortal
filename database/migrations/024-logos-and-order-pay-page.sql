-- CUSTOMER-PORTAL-FOUNDATION — logos and the public order payment page (operator request 2026-11-06). MIGRATION_NOT_EXECUTED.
-- Change: CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T053314Z-D0B894.
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, after 023.
--   dbo.CustomerLogos       logo image of a company using the portal (BuyerId NULL) or of one of its own customers
--                           (BuyerId = dbo.CustomerBuyers.Id); PNG/JPEG/WEBP up to 1 MB, kept in the database so a
--                           deploy never loses it. One row per company / per buyer (filtered unique indexes).
--   dbo.CustomerOrders      PublicToken          random token of the order's public payment page (/pay/{token}),
--                                                created on first use; unique when present.
--                           InvoiceBuyerTaxCode, InvoiceBuyerName, InvoiceBuyerUpdatedAt
--                                                tax code and invoice name the payer corrected on that page; they take
--                                                precedence over the buyer profile when the e-invoice is issued.
-- New table and nullable columns only; no existing row is changed. Idempotent.
-- Verify: database/scripts/037-verify-logos-and-order-pay-page.sql. Rollback: database/scripts/038-rollback-logos-and-order-pay-page.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
-- Equivalent required positive guard: DB_NAME() = 'TAPortalSys'. No USE statement.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.CustomerBuyers', N'U') IS NULL OR OBJECT_ID(N'dbo.CustomerOrders', N'U') IS NULL OR OBJECT_ID(N'dbo.Customers', N'U') IS NULL
 THROW 51001, 'Precondition failed: dbo.Customers / dbo.CustomerBuyers / dbo.CustomerOrders missing (run 011-012 first)', 1;

IF OBJECT_ID(N'dbo.CustomerLogos', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustomerLogos (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_CustomerLogos PRIMARY KEY DEFAULT NEWID(),
        CustomerId uniqueidentifier NOT NULL,
        BuyerId uniqueidentifier NULL,
        ContentType varchar(20) NOT NULL,
        Data varbinary(max) NOT NULL,
        UpdatedAt datetime2(3) NOT NULL CONSTRAINT DF_CL_UpdatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedBy uniqueidentifier NULL,
        CONSTRAINT FK_CL_Customer FOREIGN KEY (CustomerId) REFERENCES dbo.Customers(Id),
        CONSTRAINT FK_CL_Buyer FOREIGN KEY (BuyerId) REFERENCES dbo.CustomerBuyers(Id),
        CONSTRAINT CK_CL_ContentType CHECK (ContentType IN ('image/png','image/jpeg','image/webp')),
        CONSTRAINT CK_CL_Size CHECK (DATALENGTH(Data) BETWEEN 1 AND 1048576)
    );
    EXEC(N'CREATE UNIQUE INDEX UX_CustomerLogos_Company ON dbo.CustomerLogos(CustomerId) WHERE BuyerId IS NULL;');
    EXEC(N'CREATE UNIQUE INDEX UX_CustomerLogos_Buyer ON dbo.CustomerLogos(CustomerId, BuyerId) WHERE BuyerId IS NOT NULL;');
    PRINT N'dbo.CustomerLogos: created';
END ELSE PRINT N'dbo.CustomerLogos: exists';

IF COL_LENGTH(N'dbo.CustomerOrders', N'PublicToken') IS NULL
BEGIN
    ALTER TABLE dbo.CustomerOrders ADD
        PublicToken varchar(40) NULL,
        InvoiceBuyerTaxCode varchar(20) NULL,
        InvoiceBuyerName nvarchar(250) NULL,
        InvoiceBuyerUpdatedAt datetime2(3) NULL;
    EXEC(N'CREATE UNIQUE INDEX UX_CustomerOrders_PublicToken ON dbo.CustomerOrders(PublicToken) WHERE PublicToken IS NOT NULL;');
    PRINT N'dbo.CustomerOrders: 4 payment page columns added';
END ELSE PRINT N'dbo.CustomerOrders: payment page columns exist';

COMMIT;
PRINT N'024-logos-and-order-pay-page.sql: OK';
