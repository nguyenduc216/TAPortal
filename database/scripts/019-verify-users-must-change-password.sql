-- CUSTOMER-PORTAL-FOUNDATION-CHG-20261104T052602Z-922EA0 — READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/015-users-must-change-password.sql. Expected last line: VERIFY_STATUS=PASS.
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
DECLARE @fail int = 0;
PRINT N'=== USERS MUST CHANGE PASSWORD VERIFY ===';
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
           WHERE c.object_id = OBJECT_ID(N'dbo.Users') AND c.name = N'MustChangePassword' AND t.name = N'bit' AND c.is_nullable = 0)
    PRINT N'PASS: dbo.Users.MustChangePassword bit NOT NULL';
ELSE BEGIN PRINT N'FAIL: dbo.Users.MustChangePassword missing or wrong type'; SET @fail += 1; END
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.Users') AND name = N'DF_Users_MustChangePassword')
    PRINT N'PASS: DF_Users_MustChangePassword';
ELSE BEGIN PRINT N'FAIL: DF_Users_MustChangePassword missing'; SET @fail += 1; END
PRINT N'VERIFY_STATUS=' + CASE WHEN @fail = 0 THEN N'PASS' ELSE N'FAIL' END;
