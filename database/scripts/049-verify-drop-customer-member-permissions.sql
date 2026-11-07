-- CUSTOMER-PORTAL-FOUNDATION — READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/030-drop-customer-member-permissions.sql. Expected last line: VERIFY_STATUS=PASS (NOT_APPLIED before the migration).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
DECLARE @fail int = 0;
DECLARE @def nvarchar(max) = (SELECT definition FROM sys.check_constraints
                              WHERE name = N'CK_CPM_Role' AND parent_object_id = OBJECT_ID(N'dbo.CustomerPortalMemberships'));
PRINT N'=== DROP CUSTOMER MEMBER PERMISSIONS VERIFY ===';
IF COL_LENGTH(N'dbo.CustomerPortalMemberships', N'Permissions') IS NOT NULL OR @def LIKE N'%MEMBER%'
BEGIN
    PRINT N'INFO: dbo.CustomerPortalMemberships still has Permissions or role MEMBER';
    PRINT N'VERIFY_STATUS=NOT_APPLIED';
    RETURN;
END
PRINT N'PASS: dbo.CustomerPortalMemberships.Permissions removed';
IF @def IS NOT NULL AND @def LIKE N'%OWNER%' AND @def LIKE N'%ADMIN%' AND @def LIKE N'%VIEWER%'
    PRINT N'PASS: CK_CPM_Role allows OWNER, ADMIN, VIEWER'; ELSE BEGIN PRINT N'FAIL: CK_CPM_Role missing or incomplete'; SET @fail += 1; END
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_CPM_Role' AND (is_disabled = 1 OR is_not_trusted = 1))
    BEGIN PRINT N'FAIL: CK_CPM_Role disabled or not trusted'; SET @fail += 1; END
ELSE PRINT N'PASS: CK_CPM_Role enabled and trusted';
PRINT N'VERIFY_STATUS=' + CASE WHEN @fail = 0 THEN N'PASS' ELSE N'FAIL' END;
