-- TINGEE-INTEGRATION - READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/068-einvoice-softdreams-provider.sql. Expected last line: VERIFY_STATUS=PASS (NOT_APPLIED before the migration).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
DECLARE @t int = OBJECT_ID(N'dbo.CustomerInvoiceSettings');
PRINT N'=== E-INVOICE SOFTDREAMS PROVIDER VERIFY ===';
IF @t IS NULL BEGIN PRINT N'FAIL: dbo.CustomerInvoiceSettings missing'; PRINT N'VERIFY_STATUS=FAIL'; RETURN; END
DECLARE @col bit = CASE WHEN COL_LENGTH(N'dbo.CustomerInvoiceSettings', N'ProviderSecret') IS NULL THEN 0 ELSE 1 END;
DECLARE @ck bit = CASE WHEN EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_CIS_Provider' AND parent_object_id = @t AND definition LIKE N'%softdreams%') THEN 1 ELSE 0 END;
IF @col = 0 AND @ck = 0
BEGIN
    PRINT N'INFO: ProviderSecret missing and CK_CIS_Provider does not allow softdreams';
    PRINT N'VERIFY_STATUS=NOT_APPLIED';
    RETURN;
END
DECLARE @fail int = 0;
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
           WHERE c.object_id = @t AND c.name = N'ProviderSecret' AND ty.name = N'varbinary' AND c.max_length = -1 AND c.is_nullable = 1)
    PRINT N'PASS: ProviderSecret varbinary(max) NULL'; ELSE BEGIN PRINT N'FAIL: ProviderSecret missing or wrong type'; SET @fail += 1; END
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_CIS_Provider' AND parent_object_id = @t AND is_disabled = 0 AND is_not_trusted = 0
           AND definition LIKE N'%''misa''%' AND definition LIKE N'%''s-invoice''%' AND definition LIKE N'%''softdreams''%')
    PRINT N'PASS: CK_CIS_Provider allows misa, s-invoice, softdreams (enabled, trusted)'; ELSE BEGIN PRINT N'FAIL: CK_CIS_Provider disabled, untrusted or incomplete'; SET @fail += 1; END
IF (SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = @t AND definition LIKE N'%Provider%') = 1
    PRINT N'PASS: one provider CHECK constraint'; ELSE BEGIN PRINT N'FAIL: more than one provider CHECK constraint'; SET @fail += 1; END
PRINT N'VERIFY_STATUS=' + CASE WHEN @fail = 0 THEN N'PASS' ELSE N'FAIL' END;
