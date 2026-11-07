-- CUSTOMER-PORTAL-FOUNDATION — READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/029-customer-member-permissions.sql. Expected last line: VERIFY_STATUS=PASS (NOT_APPLIED before the migration).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
DECLARE @fail int = 0;
DECLARE @def nvarchar(max) = (SELECT definition FROM sys.check_constraints
                              WHERE name = N'CK_CPM_Role' AND parent_object_id = OBJECT_ID(N'dbo.CustomerPortalMemberships'));
PRINT N'=== CUSTOMER MEMBER PERMISSIONS VERIFY ===';
IF OBJECT_ID(N'dbo.CustomerPortalMemberships', N'U') IS NULL OR @def IS NULL
BEGIN
    PRINT N'FAIL: dbo.CustomerPortalMemberships or CK_CPM_Role missing';
    PRINT N'VERIFY_STATUS=FAIL';
    RETURN;
END
IF COL_LENGTH(N'dbo.CustomerPortalMemberships', N'Permissions') IS NULL AND @def NOT LIKE N'%MEMBER%'
BEGIN
    PRINT N'INFO: Permissions column missing and CK_CPM_Role does not allow MEMBER yet';
    PRINT N'VERIFY_STATUS=NOT_APPLIED';
    RETURN;
END
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
           WHERE c.object_id = OBJECT_ID(N'dbo.CustomerPortalMemberships') AND c.name = N'Permissions'
             AND t.name = N'varchar' AND c.max_length = 500 AND c.is_nullable = 1)
    PRINT N'PASS: dbo.CustomerPortalMemberships.Permissions varchar(500) NULL';
ELSE BEGIN PRINT N'FAIL: dbo.CustomerPortalMemberships.Permissions missing or not varchar(500) NULL'; SET @fail += 1; END
IF @def LIKE N'%MEMBER%' PRINT N'PASS: CK_CPM_Role allows MEMBER';
ELSE BEGIN PRINT N'FAIL: CK_CPM_Role does not allow MEMBER'; SET @fail += 1; END
IF @def LIKE N'%OWNER%' AND @def LIKE N'%ADMIN%' AND @def LIKE N'%VIEWER%'
    PRINT N'PASS: CK_CPM_Role keeps OWNER, ADMIN, VIEWER'; ELSE BEGIN PRINT N'FAIL: an earlier portal role is no longer allowed'; SET @fail += 1; END
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_CPM_Role' AND parent_object_id = OBJECT_ID(N'dbo.CustomerPortalMemberships')
           AND (is_disabled = 1 OR is_not_trusted = 1))
    BEGIN PRINT N'FAIL: CK_CPM_Role disabled or not trusted'; SET @fail += 1; END
ELSE PRINT N'PASS: CK_CPM_Role enabled and trusted';
DECLARE @members int = 0, @granted int = 0;
IF COL_LENGTH(N'dbo.CustomerPortalMemberships', N'Permissions') IS NOT NULL
EXEC sp_executesql N'SELECT @m = COUNT(*), @g = COALESCE(SUM(CASE WHEN Permissions IS NOT NULL THEN 1 ELSE 0 END), 0)
                     FROM dbo.CustomerPortalMemberships WHERE PortalRole = ''MEMBER''',
                   N'@m int OUTPUT, @g int OUTPUT', @m = @members OUTPUT, @g = @granted OUTPUT;
PRINT N'INFO: MEMBER memberships=' + CAST(@members AS nvarchar(20)) + N', with permissions=' + CAST(@granted AS nvarchar(20));
PRINT N'VERIFY_STATUS=' + CASE WHEN @fail = 0 THEN N'PASS' ELSE N'FAIL' END;
