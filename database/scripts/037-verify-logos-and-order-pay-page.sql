-- CUSTOMER-PORTAL-FOUNDATION — READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/024-logos-and-order-pay-page.sql. Expected last line: VERIFY_STATUS=PASS (NOT_APPLIED before the migration).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
DECLARE @fail int = 0;
DECLARE @table bit = CASE WHEN OBJECT_ID(N'dbo.CustomerLogos', N'U') IS NULL THEN 0 ELSE 1 END;
DECLARE @cols int = (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.CustomerOrders') AND is_nullable = 1 AND name IN
    (N'PublicToken',N'InvoiceBuyerTaxCode',N'InvoiceBuyerName',N'InvoiceBuyerUpdatedAt'));
PRINT N'=== LOGOS AND ORDER PAY PAGE VERIFY ===';
IF @table = 0 AND @cols = 0
BEGIN
    PRINT N'INFO: dbo.CustomerLogos and payment page columns missing';
    PRINT N'VERIFY_STATUS=NOT_APPLIED';
    RETURN;
END
IF @table = 1 PRINT N'PASS: dbo.CustomerLogos'; ELSE BEGIN PRINT N'FAIL: dbo.CustomerLogos missing'; SET @fail += 1; END
IF @cols = 4 PRINT N'PASS: dbo.CustomerOrders 4 nullable payment page columns';
ELSE BEGIN PRINT N'FAIL: dbo.CustomerOrders has ' + CAST(@cols AS nvarchar(5)) + N' of 4 payment page columns'; SET @fail += 1; END
IF (SELECT COUNT(*) FROM sys.indexes WHERE is_unique = 1 AND has_filter = 1 AND
    ((object_id = OBJECT_ID(N'dbo.CustomerLogos') AND name IN (N'UX_CustomerLogos_Company', N'UX_CustomerLogos_Buyer'))
     OR (object_id = OBJECT_ID(N'dbo.CustomerOrders') AND name = N'UX_CustomerOrders_PublicToken'))) = 3
    PRINT N'PASS: 3 filtered unique indexes'; ELSE BEGIN PRINT N'FAIL: a filtered unique index is missing'; SET @fail += 1; END
IF (SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.CustomerLogos') AND is_disabled = 0 AND name IN (N'CK_CL_ContentType', N'CK_CL_Size')) = 2
    PRINT N'PASS: CK_CL_ContentType, CK_CL_Size'; ELSE BEGIN PRINT N'FAIL: a dbo.CustomerLogos check constraint is missing'; SET @fail += 1; END
IF (SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'dbo.CustomerLogos') AND name IN (N'FK_CL_Customer', N'FK_CL_Buyer')) = 2
    PRINT N'PASS: FK_CL_Customer, FK_CL_Buyer'; ELSE BEGIN PRINT N'FAIL: a dbo.CustomerLogos foreign key is missing'; SET @fail += 1; END
PRINT N'VERIFY_STATUS=' + CASE WHEN @fail = 0 THEN N'PASS' ELSE N'FAIL' END;
