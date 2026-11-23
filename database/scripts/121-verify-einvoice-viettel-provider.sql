-- TINGEE-INTEGRATION - READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/067-einvoice-viettel-provider.sql. Expected last line: VERIFY_STATUS=PASS (NOT_APPLIED before the migration).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
DECLARE @t int = OBJECT_ID(N'dbo.CustomerInvoiceSettings');
PRINT N'=== E-INVOICE VIETTEL PROVIDER VERIFY ===';
IF @t IS NULL BEGIN PRINT N'FAIL: dbo.CustomerInvoiceSettings missing'; PRINT N'VERIFY_STATUS=FAIL'; RETURN; END
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_CIS_Provider' AND parent_object_id = @t AND definition LIKE N'%s-invoice%')
BEGIN
    PRINT N'INFO: CK_CIS_Provider does not allow s-invoice';
    PRINT N'VERIFY_STATUS=NOT_APPLIED';
    RETURN;
END
DECLARE @fail int = 0;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_CIS_Provider' AND parent_object_id = @t AND is_disabled = 0 AND is_not_trusted = 0
           AND definition LIKE N'%''misa''%' AND definition LIKE N'%''s-invoice''%')
    PRINT N'PASS: CK_CIS_Provider allows misa, s-invoice (enabled, trusted)'; ELSE BEGIN PRINT N'FAIL: CK_CIS_Provider disabled, untrusted or incomplete'; SET @fail += 1; END
IF (SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = @t AND definition LIKE N'%Provider%') = 1
    PRINT N'PASS: one provider CHECK constraint'; ELSE BEGIN PRINT N'FAIL: more than one provider CHECK constraint'; SET @fail += 1; END
PRINT N'VERIFY_STATUS=' + CASE WHEN @fail = 0 THEN N'PASS' ELSE N'FAIL' END;
