-- CUSTOMER-PORTAL-FOUNDATION — READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/026-order-invoice-draft.sql. Expected last line: VERIFY_STATUS=PASS (NOT_APPLIED before the migration).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
DECLARE @fail int = 0;
DECLARE @def nvarchar(max) = (SELECT definition FROM sys.check_constraints
                              WHERE name = N'CK_CO_InvoiceStatus' AND parent_object_id = OBJECT_ID(N'dbo.CustomerOrders'));
PRINT N'=== ORDER INVOICE DRAFT STATUS VERIFY ===';
IF @def IS NULL
BEGIN
    PRINT N'FAIL: CK_CO_InvoiceStatus missing on dbo.CustomerOrders';
    PRINT N'VERIFY_STATUS=FAIL';
    RETURN;
END
IF @def NOT LIKE N'%DRAFT%'
BEGIN
    PRINT N'INFO: CK_CO_InvoiceStatus does not allow DRAFT yet';
    PRINT N'VERIFY_STATUS=NOT_APPLIED';
    RETURN;
END
IF @def LIKE N'%NONE%' AND @def LIKE N'%PENDING%' AND @def LIKE N'%ISSUED%' AND @def LIKE N'%FAILED%'
    PRINT N'PASS: CK_CO_InvoiceStatus keeps NONE, PENDING, ISSUED, FAILED'; ELSE BEGIN PRINT N'FAIL: an earlier invoice status is no longer allowed'; SET @fail += 1; END
PRINT N'PASS: CK_CO_InvoiceStatus allows DRAFT';
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_CO_InvoiceStatus' AND (is_disabled = 1 OR is_not_trusted = 1))
    BEGIN PRINT N'FAIL: CK_CO_InvoiceStatus disabled or not trusted'; SET @fail += 1; END
ELSE PRINT N'PASS: CK_CO_InvoiceStatus enabled and trusted';
PRINT N'VERIFY_STATUS=' + CASE WHEN @fail = 0 THEN N'PASS' ELSE N'FAIL' END;
