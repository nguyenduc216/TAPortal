-- CUSTOMER-PORTAL-FOUNDATION — READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/021-customer-buyer-profile.sql. Expected last line: VERIFY_STATUS=PASS (NOT_APPLIED before the migration).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
DECLARE @fail int = 0;
DECLARE @cols int = (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.CustomerBuyers') AND name IN
    (N'BuyerType',N'BuyerGroup',N'IdentityNumber',N'Zalo',N'BankName',N'BankAccountNumber',N'BankAccountName'));
PRINT N'=== CUSTOMER BUYER PROFILE VERIFY ===';
IF @cols = 0
BEGIN
    PRINT N'INFO: buyer profile columns missing';
    PRINT N'VERIFY_STATUS=NOT_APPLIED';
    RETURN;
END
IF @cols = 7 PRINT N'PASS: dbo.CustomerBuyers 7 buyer profile columns';
ELSE BEGIN PRINT N'FAIL: dbo.CustomerBuyers has ' + CAST(@cols AS nvarchar(5)) + N' of 7 buyer profile columns'; SET @fail += 1; END
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.CustomerBuyers') AND is_nullable = 0 AND name IN
    (N'BuyerType',N'BuyerGroup',N'IdentityNumber',N'Zalo',N'BankName',N'BankAccountNumber',N'BankAccountName'))
    PRINT N'PASS: all buyer profile columns nullable'; ELSE BEGIN PRINT N'FAIL: a buyer profile column is NOT NULL'; SET @fail += 1; END
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.CustomerBuyers') AND name = N'CK_CB_BuyerType' AND is_disabled = 0)
    PRINT N'PASS: CK_CB_BuyerType'; ELSE BEGIN PRINT N'FAIL: CK_CB_BuyerType missing'; SET @fail += 1; END
PRINT N'VERIFY_STATUS=' + CASE WHEN @fail = 0 THEN N'PASS' ELSE N'FAIL' END;
