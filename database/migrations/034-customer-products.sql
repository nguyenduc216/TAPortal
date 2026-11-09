-- CUSTOMER-PORTAL-FOUNDATION - product catalogue of a company and its per-customer prices (operator request 2026-11-09).
-- MIGRATION_NOT_EXECUTED.
-- Change: CUSTOMER-PORTAL-FOUNDATION-CHG-20261109T162008Z-1FF4B8.
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, after 033.
--   dbo.CustomerProducts     products a company sells: name, unit, reference price INCLUDING VAT (whole dong), tax rate,
--                            optional image (PNG/JPEG/WEBP up to 1 MB, kept in the database), active flag.
--   dbo.CustomerBuyerPrices  price of one product agreed with one of the company's own customers (dbo.CustomerBuyers),
--                            including VAT; offered instead of the catalogue price on that customer's next orders.
--   dbo.CustomerOrderLines   ProductId (nullable): the catalogue product a line was taken from (none for typed lines).
-- New tables and one nullable column only; no existing row is changed. Idempotent.
-- Verify: database/scripts/057-verify-customer-products.sql. Rollback: database/scripts/058-rollback-customer-products.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.Customers', N'U') IS NULL OR OBJECT_ID(N'dbo.CustomerBuyers', N'U') IS NULL OR OBJECT_ID(N'dbo.CustomerOrderLines', N'U') IS NULL
 THROW 51001, 'Precondition failed: dbo.Customers / dbo.CustomerBuyers / dbo.CustomerOrderLines missing (run 011-012 first)', 1;

IF OBJECT_ID(N'dbo.CustomerProducts', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustomerProducts (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_CustomerProducts PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        CustomerId uniqueidentifier NOT NULL,
        Name nvarchar(500) NOT NULL,
        UnitName nvarchar(50) NULL,
        Price decimal(18,0) NOT NULL,
        TaxRateCode varchar(3) NOT NULL,
        IsActive bit NOT NULL CONSTRAINT DF_CP_IsActive DEFAULT 1,
        ImageType varchar(20) NULL,
        Image varbinary(max) NULL,
        ImageUpdatedAt datetime2(3) NULL,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_CP_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt datetime2(3) NOT NULL CONSTRAINT DF_CP_UpdatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedBy uniqueidentifier NULL,
        CONSTRAINT FK_CP_Customer FOREIGN KEY (CustomerId) REFERENCES dbo.Customers(Id),
        CONSTRAINT CK_CP_Price CHECK (Price >= 0),
        CONSTRAINT CK_CP_TaxRate CHECK (TaxRateCode IN ('0','5','8','10','-1','-2')),
        CONSTRAINT CK_CP_Image CHECK ((Image IS NULL AND ImageType IS NULL) OR
            (ImageType IN ('image/png','image/jpeg','image/webp') AND DATALENGTH(Image) BETWEEN 1 AND 1048576))
    );
    EXEC(N'CREATE INDEX IX_CustomerProducts_Customer ON dbo.CustomerProducts(CustomerId, IsActive);');
    PRINT N'dbo.CustomerProducts: created';
END ELSE PRINT N'dbo.CustomerProducts: exists';

IF OBJECT_ID(N'dbo.CustomerBuyerPrices', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustomerBuyerPrices (
        CustomerId uniqueidentifier NOT NULL,
        BuyerId uniqueidentifier NOT NULL,
        ProductId uniqueidentifier NOT NULL,
        Price decimal(18,0) NOT NULL,
        UpdatedAt datetime2(3) NOT NULL CONSTRAINT DF_CBP_UpdatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedBy uniqueidentifier NULL,
        CONSTRAINT PK_CustomerBuyerPrices PRIMARY KEY (BuyerId, ProductId),
        CONSTRAINT FK_CBP_Customer FOREIGN KEY (CustomerId) REFERENCES dbo.Customers(Id),
        CONSTRAINT FK_CBP_Buyer FOREIGN KEY (BuyerId) REFERENCES dbo.CustomerBuyers(Id),
        CONSTRAINT FK_CBP_Product FOREIGN KEY (ProductId) REFERENCES dbo.CustomerProducts(Id),
        CONSTRAINT CK_CBP_Price CHECK (Price >= 0)
    );
    EXEC(N'CREATE INDEX IX_CustomerBuyerPrices_Product ON dbo.CustomerBuyerPrices(ProductId);');
    PRINT N'dbo.CustomerBuyerPrices: created';
END ELSE PRINT N'dbo.CustomerBuyerPrices: exists';

IF COL_LENGTH(N'dbo.CustomerOrderLines', N'ProductId') IS NULL
BEGIN
    ALTER TABLE dbo.CustomerOrderLines ADD ProductId uniqueidentifier NULL;
    EXEC(N'ALTER TABLE dbo.CustomerOrderLines ADD CONSTRAINT FK_COL_Product FOREIGN KEY (ProductId) REFERENCES dbo.CustomerProducts(Id);');
    PRINT N'dbo.CustomerOrderLines: ProductId added';
END ELSE PRINT N'dbo.CustomerOrderLines: ProductId exists';

COMMIT;
PRINT N'034-customer-products.sql: OK';
