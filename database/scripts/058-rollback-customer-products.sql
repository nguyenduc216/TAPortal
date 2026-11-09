-- CUSTOMER-PORTAL-FOUNDATION - ROLLBACK of migrations/034-customer-products.sql.
-- Drops dbo.CustomerBuyerPrices, dbo.CustomerProducts and dbo.CustomerOrderLines.ProductId. Refuses when companies have
-- entered products (customer data): export them and decide with the operator first.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @used int = 0;
IF OBJECT_ID(N'dbo.CustomerProducts', N'U') IS NOT NULL
    EXEC sys.sp_executesql N'SELECT @n = COUNT(*) FROM dbo.CustomerProducts', N'@n int OUTPUT', @n = @used OUTPUT;
IF @used > 0
    THROW 51002, 'Rollback refused: dbo.CustomerProducts holds products entered by companies; export them first', 1;

IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_COL_Product')
    ALTER TABLE dbo.CustomerOrderLines DROP CONSTRAINT FK_COL_Product;
IF COL_LENGTH(N'dbo.CustomerOrderLines', N'ProductId') IS NOT NULL ALTER TABLE dbo.CustomerOrderLines DROP COLUMN ProductId;
IF OBJECT_ID(N'dbo.CustomerBuyerPrices', N'U') IS NOT NULL DROP TABLE dbo.CustomerBuyerPrices;
IF OBJECT_ID(N'dbo.CustomerProducts', N'U') IS NOT NULL DROP TABLE dbo.CustomerProducts;
COMMIT;
PRINT N'058-rollback-customer-products.sql: OK';
