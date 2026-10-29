-- TINGEE-INTEGRATION-CHG-20261029T203104Z-1FE09C — READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/011-tingee-payments-foundation.sql. Expected last line: VERIFY_STATUS=PASS.
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
DECLARE @fail int = 0, @name sysname;
PRINT N'=== TINGEE FOUNDATION VERIFY ===';
DECLARE @objects TABLE (Name sysname);
INSERT @objects VALUES (N'TingeeMerchants'),(N'CustomerBankAccounts'),(N'CustomerContacts'),(N'CustomerOrders'),(N'CustomerOrderLines'),(N'CustomerPaymentSettings'),(N'TingeeWebhookInbox');
DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT Name FROM @objects;
OPEN c; FETCH NEXT FROM c INTO @name;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF OBJECT_ID(N'dbo.' + @name, N'U') IS NULL BEGIN PRINT N'FAIL: dbo.' + @name + N' missing'; SET @fail += 1; END
    ELSE PRINT N'PASS: dbo.' + @name;
    FETCH NEXT FROM c INTO @name;
END
CLOSE c; DEALLOCATE c;
DECLARE @indexes TABLE (TableName sysname, IndexName sysname);
INSERT @indexes VALUES (N'TingeeMerchants',N'UQ_TingeeMerchants_Customer'),(N'TingeeMerchants',N'UQ_TingeeMerchants_Merchant'),
    (N'CustomerContacts',N'UX_CC_Customer_Code'),(N'CustomerOrders',N'UX_CO_Customer_OrderNo'),(N'CustomerOrders',N'UX_CO_BillId'),
    (N'CustomerBankAccounts',N'UX_CBA_Customer_Va'),(N'TingeeWebhookInbox',N'UX_TWI_Kind_Dedup');
DECLARE @t sysname, @i sysname;
DECLARE x CURSOR LOCAL FAST_FORWARD FOR SELECT TableName, IndexName FROM @indexes;
OPEN x; FETCH NEXT FROM x INTO @t, @i;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.' + @t) AND name = @i AND is_unique = 1 AND is_disabled = 0)
        PRINT N'PASS: ' + @i;
    ELSE BEGIN PRINT N'FAIL: unique index ' + @i + N' missing/disabled'; SET @fail += 1; END
    FETCH NEXT FROM x INTO @t, @i;
END
CLOSE x; DEALLOCATE x;
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id IN (OBJECT_ID(N'dbo.TingeeMerchants'),OBJECT_ID(N'dbo.CustomerBankAccounts'),OBJECT_ID(N'dbo.CustomerContacts'),OBJECT_ID(N'dbo.CustomerOrders'),OBJECT_ID(N'dbo.CustomerOrderLines'),OBJECT_ID(N'dbo.CustomerPaymentSettings')) AND (is_disabled = 1 OR is_not_trusted = 1))
BEGIN PRINT N'FAIL: a foreign key is disabled or untrusted'; SET @fail += 1; END
ELSE PRINT N'PASS: foreign keys enabled and trusted';
PRINT N'VERIFY_STATUS=' + CASE WHEN @fail = 0 THEN N'PASS' ELSE N'FAIL (' + CAST(@fail AS nvarchar(10)) + N')' END;
