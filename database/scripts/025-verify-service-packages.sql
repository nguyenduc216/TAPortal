-- CUSTOMER-PORTAL-FOUNDATION-CHG-20261104T112825Z-F9FA74 — READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/018-service-packages.sql. Expected last line: VERIFY_STATUS=PASS (NOT_APPLIED before the migration).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
DECLARE @fail int = 0;
PRINT N'=== SERVICE PACKAGES VERIFY ===';
IF OBJECT_ID(N'dbo.ServicePackages', N'U') IS NULL AND OBJECT_ID(N'dbo.ServicePackagePurchases', N'U') IS NULL
BEGIN
    PRINT N'INFO: tables missing';
    PRINT N'VERIFY_STATUS=NOT_APPLIED';
    RETURN;
END
IF (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.ServicePackages') AND name IN
    (N'Id',N'Code',N'Name',N'PackageType',N'InvoiceQuota',N'TransactionQuota',N'Price',N'ValidityDays',N'Description',N'IsActive',N'SortOrder',N'CreatedAt',N'CreatedBy',N'UpdatedAt',N'UpdatedBy')) = 15
    PRINT N'PASS: dbo.ServicePackages 15 columns';
ELSE BEGIN PRINT N'FAIL: dbo.ServicePackages columns missing'; SET @fail += 1; END
IF (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.ServicePackagePurchases') AND name IN
    (N'Id',N'PurchaseNo',N'CustomerId',N'PackageId',N'PackageCode',N'PackageName',N'PackageType',N'InvoiceQuota',N'TransactionQuota',N'Amount',N'ValidityDays',
     N'Status',N'RequestedVia',N'RequestedBy',N'CreatedAt',N'PaidAmount',N'PaidAt',N'PaymentMethod',N'PaymentReference',N'ConfirmedBy',N'ExpiresAt',
     N'InvoicesRemaining',N'TransactionsRemaining',N'CancelledAt',N'CancelledBy',N'CancelReason',N'UpdatedAt')) = 27
    PRINT N'PASS: dbo.ServicePackagePurchases 27 columns';
ELSE BEGIN PRINT N'FAIL: dbo.ServicePackagePurchases columns missing'; SET @fail += 1; END
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ServicePackages') AND name = N'UX_ServicePackages_Code' AND is_unique = 1)
    PRINT N'PASS: UX_ServicePackages_Code'; ELSE BEGIN PRINT N'FAIL: UX_ServicePackages_Code missing'; SET @fail += 1; END
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ServicePackagePurchases') AND name = N'UX_SPP_PurchaseNo' AND is_unique = 1)
    PRINT N'PASS: UX_SPP_PurchaseNo'; ELSE BEGIN PRINT N'FAIL: UX_SPP_PurchaseNo missing'; SET @fail += 1; END
IF (SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id IN (OBJECT_ID(N'dbo.ServicePackages'), OBJECT_ID(N'dbo.ServicePackagePurchases'))
    AND name IN (N'CK_SP_Type',N'CK_SP_Price',N'CK_SP_Validity',N'CK_SPP_Status',N'CK_SPP_Via',N'CK_SPP_Remaining',N'CK_SPP_Paid') AND is_disabled = 0) = 7
    PRINT N'PASS: 7 check constraints'; ELSE BEGIN PRINT N'FAIL: check constraints missing/disabled'; SET @fail += 1; END
IF (SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'dbo.ServicePackagePurchases') AND name IN (N'FK_SPP_Customer',N'FK_SPP_Package')) = 2
    PRINT N'PASS: FK_SPP_Customer, FK_SPP_Package'; ELSE BEGIN PRINT N'FAIL: foreign keys missing'; SET @fail += 1; END
PRINT N'VERIFY_STATUS=' + CASE WHEN @fail = 0 THEN N'PASS' ELSE N'FAIL' END;
