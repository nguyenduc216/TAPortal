-- TINGEE-INTEGRATION-CHG-20261030T103236Z-05E4FE — READ ONLY; SQL Server 2014; output only Messages.
-- Checks dbo.CustomerContacts against the application, table row counts, roles and the permissions the apps need.
-- Expected last line: DIAGNOSE_STATUS=PASS. Prints names and counts only, never row data.
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
DECLARE @fail int = 0, @warn int = 0, @name sysname, @type sysname, @line nvarchar(1000), @user sysname, @userId int, @missing nvarchar(1000), @rows bigint, @created datetime;
DECLARE @contacts int = OBJECT_ID(N'dbo.CustomerContacts', N'U');
PRINT N'=== TINGEE FOUNDATION DIAGNOSE ===';

PRINT N'--- 1. Tables ---';
DECLARE @tables TABLE (Name sysname);
INSERT @tables VALUES (N'TingeeMerchants'),(N'CustomerBankAccounts'),(N'CustomerContacts'),(N'CustomerOrders'),(N'CustomerOrderLines'),(N'CustomerPaymentSettings'),(N'TingeeWebhookInbox');
DECLARE t CURSOR LOCAL FAST_FORWARD FOR SELECT Name FROM @tables;
OPEN t; FETCH NEXT FROM t INTO @name;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF OBJECT_ID(N'dbo.' + @name, N'U') IS NULL
    BEGIN PRINT N'FAIL: dbo.' + @name + N' missing'; SET @fail += 1; END
    ELSE
    BEGIN
        SELECT @rows = SUM(p.rows) FROM sys.partitions p WHERE p.object_id = OBJECT_ID(N'dbo.' + @name, N'U') AND p.index_id IN (0, 1);
        SELECT @created = create_date FROM sys.objects WHERE object_id = OBJECT_ID(N'dbo.' + @name, N'U');
        PRINT N'INFO: dbo.' + @name + N' rows=' + CAST(@rows AS nvarchar(20)) + N' created=' + CONVERT(nvarchar(19), @created, 120);
    END
    FETCH NEXT FROM t INTO @name;
END
CLOSE t; DEALLOCATE t;

PRINT N'--- 2. dbo.CustomerContacts columns used by the application ---';
DECLARE @columns TABLE (Name sysname, TypeName sysname);
INSERT @columns VALUES (N'Id',N'uniqueidentifier'),(N'CustomerId',N'uniqueidentifier'),(N'Code',N'varchar'),(N'Name',N'nvarchar'),(N'LegalName',N'nvarchar'),(N'TaxCode',N'varchar'),(N'Phone',N'varchar'),(N'Email',N'nvarchar'),(N'Address',N'nvarchar'),(N'Note',N'nvarchar'),(N'IsActive',N'bit'),(N'IsDeleted',N'bit'),(N'CreatedAt',N'datetime2'),(N'CreatedBy',N'uniqueidentifier'),(N'UpdatedAt',N'datetime2'),(N'UpdatedBy',N'uniqueidentifier');
DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT Name, TypeName FROM @columns;
OPEN c; FETCH NEXT FROM c INTO @name, @type;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @line = NULL;
    SELECT @line = TYPE_NAME(sc.user_type_id) FROM sys.columns sc WHERE sc.object_id = @contacts AND sc.name = @name;
    IF @line IS NULL BEGIN PRINT N'FAIL: column ' + @name + N' missing'; SET @fail += 1; END
    ELSE IF @line <> @type BEGIN PRINT N'WARN: column ' + @name + N' is ' + @line + N', expected ' + @type; SET @warn += 1; END
    ELSE PRINT N'PASS: column ' + @name + N' ' + @line;
    FETCH NEXT FROM c INTO @name, @type;
END
CLOSE c; DEALLOCATE c;
-- Extra required columns would make the application's INSERT fail.
DECLARE x CURSOR LOCAL FAST_FORWARD FOR
    SELECT sc.name FROM sys.columns sc
    WHERE sc.object_id = @contacts AND sc.is_nullable = 0 AND sc.is_identity = 0 AND sc.is_computed = 0 AND sc.default_object_id = 0
      AND sc.name NOT IN (SELECT Name FROM @columns);
OPEN x; FETCH NEXT FROM x INTO @name;
WHILE @@FETCH_STATUS = 0
BEGIN PRINT N'FAIL: extra required column ' + @name + N' has no default'; SET @fail += 1; FETCH NEXT FROM x INTO @name; END
CLOSE x; DEALLOCATE x;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = @contacts AND name = N'UX_CC_Customer_Code' AND is_unique = 1)
    PRINT N'PASS: index UX_CC_Customer_Code';
ELSE BEGIN PRINT N'FAIL: index UX_CC_Customer_Code missing'; SET @fail += 1; END
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = @contacts AND referenced_object_id = OBJECT_ID(N'dbo.Customers'))
    PRINT N'PASS: foreign key to dbo.Customers';
ELSE BEGIN PRINT N'WARN: no foreign key to dbo.Customers'; SET @warn += 1; END

PRINT N'--- 3. Database users, roles and required permissions ---';
DECLARE @required TABLE (TableName sysname, Permission sysname);
INSERT @required VALUES
    (N'TingeeMerchants',N'SELECT'),(N'TingeeMerchants',N'INSERT'),(N'TingeeMerchants',N'DELETE'),
    (N'CustomerBankAccounts',N'SELECT'),(N'CustomerBankAccounts',N'INSERT'),(N'CustomerBankAccounts',N'UPDATE'),
    (N'CustomerContacts',N'SELECT'),(N'CustomerContacts',N'INSERT'),(N'CustomerContacts',N'UPDATE'),
    (N'CustomerOrders',N'SELECT'),(N'CustomerOrders',N'INSERT'),(N'CustomerOrders',N'UPDATE'),
    (N'CustomerOrderLines',N'SELECT'),(N'CustomerOrderLines',N'INSERT'),
    (N'CustomerPaymentSettings',N'SELECT'),(N'CustomerPaymentSettings',N'INSERT'),(N'CustomerPaymentSettings',N'UPDATE'),
    (N'TingeeWebhookInbox',N'SELECT'),(N'TingeeWebhookInbox',N'INSERT');
DECLARE u CURSOR LOCAL FAST_FORWARD FOR
    SELECT name, principal_id FROM sys.database_principals
    WHERE type IN ('S','U','G') AND name NOT IN (N'dbo',N'guest',N'sys',N'INFORMATION_SCHEMA');
OPEN u; FETCH NEXT FROM u INTO @user, @userId;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @line = NULL;
    SELECT @line = COALESCE(@line + N', ', N'') + r.name
    FROM sys.database_role_members rm JOIN sys.database_principals r ON r.principal_id = rm.role_principal_id
    WHERE rm.member_principal_id = @userId;
    PRINT N'INFO: user ' + @user + N' roles=' + COALESCE(@line, N'(none)');
    IF IS_ROLEMEMBER(N'db_owner', @user) = 1
    BEGIN PRINT N'WARN: user ' + @user + N' is db_owner, so the grants do not limit it'; SET @warn += 1; END
    ELSE
    BEGIN
        SET @missing = NULL;
        SELECT @missing = COALESCE(@missing + N', ', N'') + q.TableName + N':' + q.Permission
        FROM @required q
        WHERE NOT EXISTS (
            SELECT 1 FROM sys.database_permissions pe
            WHERE pe.state IN ('G','W') AND pe.permission_name = q.Permission
              AND (pe.grantee_principal_id = @userId OR pe.grantee_principal_id IN (SELECT role_principal_id FROM sys.database_role_members WHERE member_principal_id = @userId))
              AND ((pe.class = 1 AND pe.major_id = OBJECT_ID(N'dbo.' + q.TableName)) OR (pe.class = 3 AND pe.major_id = SCHEMA_ID(N'dbo')) OR pe.class = 0))
          AND NOT (q.Permission = N'SELECT' AND IS_ROLEMEMBER(N'db_datareader', @user) = 1)
          AND NOT (q.Permission <> N'SELECT' AND IS_ROLEMEMBER(N'db_datawriter', @user) = 1);
        IF @missing IS NULL PRINT N'PASS: user ' + @user + N' has every required permission';
        ELSE BEGIN PRINT N'FAIL: user ' + @user + N' missing ' + @missing; SET @fail += 1; END
    END
    FETCH NEXT FROM u INTO @user, @userId;
END
CLOSE u; DEALLOCATE u;

PRINT N'DIAGNOSE_STATUS=' + CASE WHEN @fail > 0 THEN N'FAIL (' + CAST(@fail AS nvarchar(10)) + N')' WHEN @warn > 0 THEN N'PASS_WITH_WARNINGS (' + CAST(@warn AS nvarchar(10)) + N')' ELSE N'PASS' END;
