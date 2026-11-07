-- CUSTOMER-PORTAL-FOUNDATION — READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/027-einvoice-issue-mode.sql. Expected last line: VERIFY_STATUS=PASS (NOT_APPLIED before the migration).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
DECLARE @fail int = 0;
DECLARE @t int = OBJECT_ID(N'dbo.CustomerInvoiceSettings');
PRINT N'=== E-INVOICE ISSUE MODE VERIFY ===';
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @t AND name IN (N'IssueMode', N'HsmConfirmedAt', N'HsmConfirmedBy'))
   AND OBJECT_ID(N'dbo.CK_CIS_IssueMode', N'C') IS NULL
BEGIN
    PRINT N'INFO: dbo.CustomerInvoiceSettings has no issue mode columns';
    PRINT N'VERIFY_STATUS=NOT_APPLIED';
    RETURN;
END
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types y ON y.user_type_id = c.user_type_id WHERE c.object_id = @t AND c.name = N'IssueMode' AND y.name = N'varchar' AND c.max_length >= 10 AND c.is_nullable = 1)
    PRINT N'PASS: IssueMode varchar(10) NULL'; ELSE BEGIN PRINT N'FAIL: IssueMode missing or wrong type'; SET @fail += 1; END
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types y ON y.user_type_id = c.user_type_id WHERE c.object_id = @t AND c.name = N'HsmConfirmedAt' AND y.name = N'datetime2' AND c.is_nullable = 1)
    PRINT N'PASS: HsmConfirmedAt datetime2 NULL'; ELSE BEGIN PRINT N'FAIL: HsmConfirmedAt missing or wrong type'; SET @fail += 1; END
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types y ON y.user_type_id = c.user_type_id WHERE c.object_id = @t AND c.name = N'HsmConfirmedBy' AND y.name = N'uniqueidentifier' AND c.is_nullable = 1)
    PRINT N'PASS: HsmConfirmedBy uniqueidentifier NULL'; ELSE BEGIN PRINT N'FAIL: HsmConfirmedBy missing or wrong type'; SET @fail += 1; END
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_CIS_IssueMode' AND parent_object_id = @t AND is_disabled = 0 AND is_not_trusted = 0
           AND definition LIKE N'%SIGNED%' AND definition LIKE N'%DRAFT%' AND definition LIKE N'%NONE%')
    PRINT N'PASS: CK_CIS_IssueMode allows SIGNED, DRAFT, NONE'; ELSE BEGIN PRINT N'FAIL: CK_CIS_IssueMode missing, disabled or incomplete'; SET @fail += 1; END
PRINT N'VERIFY_STATUS=' + CASE WHEN @fail = 0 THEN N'PASS' ELSE N'FAIL' END;
