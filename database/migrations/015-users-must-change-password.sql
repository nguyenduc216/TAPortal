-- CUSTOMER-PORTAL-FOUNDATION-CHG-20261104T052602Z-922EA0 — dbo.Users.MustChangePassword. MIGRATION_NOT_EXECUTED.
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys.
-- Why: a T.A administrator can reset a Customer Portal password to a one-time temporary password; the customer must
-- choose a new password at the next sign-in (the flag is set by the reset and cleared by "Đổi mật khẩu").
-- Adds one NOT NULL column with default 0 (every existing account keeps signing in as before). Idempotent.
-- Verify: database/scripts/019-verify-users-must-change-password.sql. Rollback: database/scripts/020-rollback-users-must-change-password.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
-- Equivalent required positive guard: DB_NAME() = 'TAPortalSys'. No USE statement.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
 THROW 51001, 'Precondition failed: dbo.Users missing', 1;
IF COL_LENGTH(N'dbo.Users', N'MustChangePassword') IS NULL
BEGIN
    ALTER TABLE dbo.Users ADD MustChangePassword bit NOT NULL CONSTRAINT DF_Users_MustChangePassword DEFAULT 0;
    PRINT N'dbo.Users.MustChangePassword added';
END ELSE PRINT N'dbo.Users.MustChangePassword exists';
COMMIT;
PRINT N'015-users-must-change-password.sql: OK';
