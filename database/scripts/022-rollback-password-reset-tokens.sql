-- CUSTOMER-PORTAL-FOUNDATION-CHG-20261104T064318Z-148859 — ROLLBACK of migrations/016-password-reset-tokens.sql.
-- Drops dbo.PasswordResetTokens. Its rows are short-lived reset links (hashes only); dropping them only invalidates links
-- not used yet. No other table is touched.
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.PasswordResetTokens', N'U') IS NOT NULL DROP TABLE dbo.PasswordResetTokens;
COMMIT;
PRINT N'022-rollback-password-reset-tokens.sql: OK (table dropped)';
