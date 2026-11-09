-- CUSTOMER-PORTAL-FOUNDATION - READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/034-customer-products.sql. Expected last line: VERIFY_STATUS=PASS (NOT_APPLIED before the migration).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
DECLARE @fail int = 0;
DECLARE @products bit = CASE WHEN OBJECT_ID(N'dbo.CustomerProducts', N'U') IS NULL THEN 0 ELSE 1 END;
DECLARE @prices bit = CASE WHEN OBJECT_ID(N'dbo.CustomerBuyerPrices', N'U') IS NULL THEN 0 ELSE 1 END;
DECLARE @col bit = CASE WHEN EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.CustomerOrderLines') AND name = N'ProductId' AND is_nullable = 1) THEN 1 ELSE 0 END;
PRINT N'=== CUSTOMER PRODUCTS VERIFY ===';
IF @products = 0 AND @prices = 0 AND @col = 0
BEGIN
    PRINT N'INFO: dbo.CustomerProducts, dbo.CustomerBuyerPrices and dbo.CustomerOrderLines.ProductId missing';
    PRINT N'VERIFY_STATUS=NOT_APPLIED';
    RETURN;
END
IF @products = 1 PRINT N'PASS: dbo.CustomerProducts'; ELSE BEGIN PRINT N'FAIL: dbo.CustomerProducts missing'; SET @fail += 1; END
IF @prices = 1 PRINT N'PASS: dbo.CustomerBuyerPrices'; ELSE BEGIN PRINT N'FAIL: dbo.CustomerBuyerPrices missing'; SET @fail += 1; END
IF @col = 1 PRINT N'PASS: dbo.CustomerOrderLines.ProductId nullable'; ELSE BEGIN PRINT N'FAIL: dbo.CustomerOrderLines.ProductId missing'; SET @fail += 1; END
IF (SELECT COUNT(*) FROM sys.check_constraints WHERE is_disabled = 0 AND name IN (N'CK_CP_Price', N'CK_CP_TaxRate', N'CK_CP_Image', N'CK_CBP_Price')) = 4
    PRINT N'PASS: 4 check constraints'; ELSE BEGIN PRINT N'FAIL: a check constraint is missing'; SET @fail += 1; END
IF (SELECT COUNT(*) FROM sys.foreign_keys WHERE name IN (N'FK_CP_Customer', N'FK_CBP_Customer', N'FK_CBP_Buyer', N'FK_CBP_Product', N'FK_COL_Product')) = 5
    PRINT N'PASS: 5 foreign keys'; ELSE BEGIN PRINT N'FAIL: a foreign key is missing'; SET @fail += 1; END
IF (SELECT COUNT(*) FROM sys.indexes WHERE name IN (N'IX_CustomerProducts_Customer', N'IX_CustomerBuyerPrices_Product')) = 2
    PRINT N'PASS: 2 indexes'; ELSE BEGIN PRINT N'FAIL: an index is missing'; SET @fail += 1; END
PRINT N'VERIFY_STATUS=' + CASE WHEN @fail = 0 THEN N'PASS' ELSE N'FAIL' END;
