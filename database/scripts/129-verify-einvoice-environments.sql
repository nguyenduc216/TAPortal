-- CUSTOMER-PORTAL-FOUNDATION - READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/071-einvoice-environments.sql. Expected last line: VERIFY_STATUS=PASS (NOT_APPLIED before the migration).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
PRINT N'=== EINVOICE ENVIRONMENTS VERIFY ===';
DECLARE @c int = OBJECT_ID(N'dbo.CustomerInvoiceConnections', N'U'),
        @d bit = CASE WHEN COL_LENGTH(N'dbo.CustomerInvoiceTemplates', N'IsDefault') IS NULL THEN 0 ELSE 1 END;
IF @c IS NULL AND @d = 0
BEGIN
    PRINT N'INFO: no dbo.CustomerInvoiceConnections and no CustomerInvoiceTemplates.IsDefault';
    PRINT N'VERIFY_STATUS=NOT_APPLIED';
    RETURN;
END
DECLARE @fail int = 0;
IF @d = 1 PRINT N'PASS: dbo.CustomerInvoiceTemplates.IsDefault exists'; ELSE BEGIN PRINT N'FAIL: CustomerInvoiceTemplates.IsDefault missing'; SET @fail += 1; END
IF @c IS NOT NULL PRINT N'PASS: dbo.CustomerInvoiceConnections exists'; ELSE BEGIN PRINT N'FAIL: dbo.CustomerInvoiceConnections missing'; SET @fail += 1; END
IF @c IS NOT NULL
BEGIN
    IF (SELECT COUNT(*) FROM sys.columns WHERE object_id = @c AND name IN (N'CustomerId',N'Provider',N'Environment',N'ProviderTaxCode',N'ProviderUsername',N'ProviderSecret',N'ConnectedAt',N'UpdatedAt',N'UpdatedBy')) = 9
        PRINT N'PASS: columns'; ELSE BEGIN PRINT N'FAIL: columns incomplete'; SET @fail += 1; END
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = @c AND name = N'CK_CIC_Environment' AND is_disabled = 0 AND is_not_trusted = 0)
        PRINT N'PASS: CK_CIC_Environment (enabled, trusted)'; ELSE BEGIN PRINT N'FAIL: CK_CIC_Environment missing, disabled or untrusted'; SET @fail += 1; END
    IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = @c AND name = N'PK_CustomerInvoiceConnections' AND type = 'PK')
       AND EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = @c AND name = N'FK_CIC_Customer' AND is_disabled = 0)
        PRINT N'PASS: PK_CustomerInvoiceConnections and FK_CIC_Customer'; ELSE BEGIN PRINT N'FAIL: key or foreign key missing'; SET @fail += 1; END
END
PRINT N'VERIFY_STATUS=' + CASE WHEN @fail = 0 THEN N'PASS' ELSE N'FAIL' END;
