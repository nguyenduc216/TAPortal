-- CUSTOMER-PORTAL-FOUNDATION-CHG-20261104T052602Z-922EA0 — ROLLBACK of migrations/015-users-must-change-password.sql.
-- Removes the column and its default. Accounts flagged by a reset then sign in with the temporary password without being
-- forced to change it; no other data is touched.
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.Users') AND name = N'DF_Users_MustChangePassword')
    ALTER TABLE dbo.Users DROP CONSTRAINT DF_Users_MustChangePassword;
IF COL_LENGTH(N'dbo.Users', N'MustChangePassword') IS NOT NULL
    ALTER TABLE dbo.Users DROP COLUMN MustChangePassword;
COMMIT;
PRINT N'020-rollback-users-must-change-password.sql: OK (column removed)';
