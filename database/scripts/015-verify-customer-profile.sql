-- CUSTOMER-PROFILE-CHG-20261103T183438Z-C56EAB — READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/013-customer-profile-core.sql. Expected last line: VERIFY_STATUS=PASS.
-- Before the migration (or after the rollback) the expected last line is VERIFY_STATUS=NOT_APPLIED.
-- Prints only object names and counts; never row contents.
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
DECLARE @fail int = 0, @present int = 0, @name sysname;
PRINT N'=== CUSTOMER PROFILE VERIFY ===';
DECLARE @objects TABLE (Name sysname);
INSERT @objects VALUES (N'CustomerProfiles'),(N'CustomerRepresentatives'),(N'CustomerSystemAccounts');
DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT Name FROM @objects;
OPEN c; FETCH NEXT FROM c INTO @name;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF OBJECT_ID(N'dbo.' + @name, N'U') IS NULL BEGIN PRINT N'INFO: dbo.' + @name + N' missing'; SET @fail += 1; END
    ELSE BEGIN PRINT N'PASS: dbo.' + @name; SET @present += 1; END
    FETCH NEXT FROM c INTO @name;
END
CLOSE c; DEALLOCATE c;

IF @present = 0
BEGIN
    PRINT N'VERIFY_STATUS=NOT_APPLIED';
    RETURN;
END

-- Ciphertext columns must be varbinary: a text column would mean secrets could be stored readable.
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.CustomerSystemAccounts') AND name IN (N'PasswordCipher',N'PinCipher') AND TYPE_NAME(system_type_id) = N'varbinary' HAVING COUNT(*) = 2)
    PRINT N'PASS: secret columns are varbinary';
ELSE BEGIN PRINT N'FAIL: PasswordCipher/PinCipher missing or not varbinary'; SET @fail += 1; END

DECLARE @checks TABLE (Name sysname);
INSERT @checks VALUES (N'CK_CPF_LegalForm'),(N'CK_CPF_BusinessStatus'),(N'CK_CPF_NotOwnParent'),(N'CK_CRP_Role'),(N'CK_CRP_Gender'),
    (N'CK_CSA_SystemType'),(N'CK_CSA_CertificateType'),(N'CK_CSA_Status'),(N'CK_CSA_ManagedBy'),(N'CK_CSA_Validity');
DECLARE k CURSOR LOCAL FAST_FORWARD FOR SELECT Name FROM @checks;
OPEN k; FETCH NEXT FROM k INTO @name;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = @name AND is_disabled = 0 AND is_not_trusted = 0) PRINT N'PASS: ' + @name;
    ELSE BEGIN PRINT N'FAIL: check constraint ' + @name + N' missing/disabled/untrusted'; SET @fail += 1; END
    FETCH NEXT FROM k INTO @name;
END
CLOSE k; DEALLOCATE k;

DECLARE @fks TABLE (Name sysname);
INSERT @fks VALUES (N'FK_CPF_Customer'),(N'FK_CPF_Parent'),(N'FK_CRP_Customer'),(N'FK_CSA_Customer');
DECLARE f CURSOR LOCAL FAST_FORWARD FOR SELECT Name FROM @fks;
OPEN f; FETCH NEXT FROM f INTO @name;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = @name AND is_disabled = 0 AND is_not_trusted = 0) PRINT N'PASS: ' + @name;
    ELSE BEGIN PRINT N'FAIL: foreign key ' + @name + N' missing/disabled/untrusted'; SET @fail += 1; END
    FETCH NEXT FROM f INTO @name;
END
CLOSE f; DEALLOCATE f;

DECLARE @indexes TABLE (TableName sysname, IndexName sysname);
INSERT @indexes VALUES (N'CustomerProfiles',N'IX_CPF_Parent'),(N'CustomerRepresentatives',N'IX_CRP_Customer'),
    (N'CustomerSystemAccounts',N'IX_CSA_Customer'),(N'CustomerSystemAccounts',N'IX_CSA_Expires');
DECLARE @t sysname, @i sysname;
DECLARE x CURSOR LOCAL FAST_FORWARD FOR SELECT TableName, IndexName FROM @indexes;
OPEN x; FETCH NEXT FROM x INTO @t, @i;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.' + @t) AND name = @i AND is_disabled = 0) PRINT N'PASS: ' + @i;
    ELSE BEGIN PRINT N'FAIL: index ' + @i + N' missing/disabled'; SET @fail += 1; END
    FETCH NEXT FROM x INTO @t, @i;
END
CLOSE x; DEALLOCATE x;

PRINT N'VERIFY_STATUS=' + CASE WHEN @fail = 0 THEN N'PASS' ELSE N'FAIL (' + CAST(@fail AS nvarchar(10)) + N')' END;
