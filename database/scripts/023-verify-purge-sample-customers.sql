-- CUSTOMER-PORTAL-FOUNDATION-CHG-20261104T103400Z-124DDE — READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/017-purge-sample-customers.sql. Expected last line: VERIFY_STATUS=PASS.
-- Before the migration it reports VERIFY_STATUS=NOT_APPLIED (no backup table, other customers still present).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
DECLARE @fail int = 0, @others int, @tvgpta int;
PRINT N'=== PURGE SAMPLE CUSTOMERS VERIFY ===';
SELECT @others = COUNT(*) FROM dbo.Customers WHERE Code <> 'TVGPTA';
SELECT @tvgpta = COUNT(*) FROM dbo.Customers WHERE Code = 'TVGPTA';
PRINT N'INFO: customers other than TVGPTA = ' + CAST(@others AS nvarchar(20)) + N', TVGPTA = ' + CAST(@tvgpta AS nvarchar(20));
IF OBJECT_ID(N'dbo.Backup20261104_Customers', N'U') IS NULL
BEGIN
    PRINT N'INFO: dbo.Backup20261104_Customers missing';
    PRINT N'VERIFY_STATUS=NOT_APPLIED';
    RETURN;
END
DECLARE @saved int, @savedContacts int = 0;
SELECT @saved = COUNT(*) FROM dbo.Backup20261104_Customers;
IF OBJECT_ID(N'dbo.Backup20261104_CustomerContacts', N'U') IS NOT NULL SELECT @savedContacts = COUNT(*) FROM dbo.Backup20261104_CustomerContacts;
ELSE BEGIN PRINT N'FAIL: dbo.Backup20261104_CustomerContacts missing'; SET @fail += 1; END
PRINT N'INFO: saved customers = ' + CAST(@saved AS nvarchar(20)) + N', saved contacts = ' + CAST(@savedContacts AS nvarchar(20));
IF @tvgpta = 1 PRINT N'PASS: TVGPTA kept'; ELSE BEGIN PRINT N'FAIL: TVGPTA missing'; SET @fail += 1; END
IF @others = 0 PRINT N'PASS: no other customers'; ELSE BEGIN PRINT N'FAIL: other customers remain'; SET @fail += 1; END
IF EXISTS (SELECT 1 FROM dbo.Customers c JOIN dbo.Backup20261104_Customers b ON b.Id = c.Id)
BEGIN PRINT N'FAIL: a saved customer is still in dbo.Customers'; SET @fail += 1; END
ELSE PRINT N'PASS: saved customers removed from dbo.Customers';
IF EXISTS (SELECT 1 FROM dbo.CustomerContacts x JOIN dbo.Backup20261104_Customers b ON b.Id = x.CustomerId)
BEGIN PRINT N'FAIL: contacts of deleted customers remain'; SET @fail += 1; END
ELSE PRINT N'PASS: their contacts removed';
PRINT N'VERIFY_STATUS=' + CASE WHEN @fail = 0 THEN N'PASS' ELSE N'FAIL' END;
