-- CONTRACTS-AND-TASKS - READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/061-service-catalog.sql. Expected last line: VERIFY_STATUS=PASS (NOT_APPLIED before the migration).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
IF OBJECT_ID(N'dbo.Services', N'U') IS NULL AND OBJECT_ID(N'dbo.ServiceCategories', N'U') IS NULL AND OBJECT_ID(N'dbo.Industries', N'U') IS NULL
   AND COL_LENGTH(N'dbo.Customers', N'IndustryId') IS NULL AND NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Code = 'CONTRACTS.SERVICES.MANAGE')
BEGIN PRINT N'INFO: service catalogue missing'; PRINT N'VERIFY_STATUS=NOT_APPLIED'; RETURN; END
DECLARE @ok bit = 1;
IF OBJECT_ID(N'dbo.ServiceCategories', N'U') IS NOT NULL PRINT N'PASS: dbo.ServiceCategories'; ELSE BEGIN PRINT N'FAIL: dbo.ServiceCategories'; SET @ok = 0; END
IF OBJECT_ID(N'dbo.Industries', N'U') IS NOT NULL PRINT N'PASS: dbo.Industries'; ELSE BEGIN PRINT N'FAIL: dbo.Industries'; SET @ok = 0; END
IF OBJECT_ID(N'dbo.Services', N'U') IS NOT NULL PRINT N'PASS: dbo.Services'; ELSE BEGIN PRINT N'FAIL: dbo.Services'; SET @ok = 0; END
IF OBJECT_ID(N'dbo.FK_Services_Category', N'F') IS NOT NULL PRINT N'PASS: FK_Services_Category'; ELSE BEGIN PRINT N'FAIL: FK_Services_Category'; SET @ok = 0; END
IF OBJECT_ID(N'dbo.CK_Services_PricingMode', N'C') IS NOT NULL PRINT N'PASS: CK_Services_PricingMode'; ELSE BEGIN PRINT N'FAIL: CK_Services_PricingMode'; SET @ok = 0; END
IF COL_LENGTH(N'dbo.Services', N'PricingJson') = -1 PRINT N'PASS: Services.PricingJson nvarchar(max)'; ELSE BEGIN PRINT N'FAIL: Services.PricingJson'; SET @ok = 0; END
IF COL_LENGTH(N'dbo.Customers', N'IndustryId') IS NOT NULL PRINT N'PASS: Customers.IndustryId'; ELSE BEGIN PRINT N'FAIL: Customers.IndustryId'; SET @ok = 0; END
IF OBJECT_ID(N'dbo.FK_Customers_Industry', N'F') IS NOT NULL PRINT N'PASS: FK_Customers_Industry'; ELSE BEGIN PRINT N'FAIL: FK_Customers_Industry'; SET @ok = 0; END
IF EXISTS (SELECT 1 FROM dbo.Permissions WHERE Code = 'CONTRACTS.SERVICES.MANAGE' AND IsDeleted = 0) PRINT N'PASS: permission CONTRACTS.SERVICES.MANAGE'; ELSE BEGIN PRINT N'FAIL: permission CONTRACTS.SERVICES.MANAGE'; SET @ok = 0; END
IF EXISTS (SELECT 1 FROM dbo.RolePermissions rp JOIN dbo.Permissions p ON p.Id = rp.PermissionId JOIN dbo.Roles r ON r.Id = rp.RoleId WHERE p.Code = 'CONTRACTS.SERVICES.MANAGE' AND r.Code = 'SYS_ADMIN') PRINT N'PASS: SYS_ADMIN granted'; ELSE BEGIN PRINT N'FAIL: SYS_ADMIN grant'; SET @ok = 0; END
PRINT N'VERIFY_STATUS=' + CASE WHEN @ok = 1 THEN N'PASS' ELSE N'FAIL' END;
