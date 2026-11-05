-- CUSTOMER-PROFILE-CHG-20261103T183438Z-C56EAB — ROLLBACK of migrations/013-customer-profile-core.sql. NOT_EXECUTED.
-- Drops the three customer profile tables ONLY when every one of them is empty; otherwise aborts without changes,
-- because those rows (including encrypted passwords) cannot be recreated. Never touches dbo.Customers.
-- Verify afterwards: database/scripts/015-verify-customer-profile.sql must print VERIFY_STATUS=NOT_APPLIED.
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF (OBJECT_ID(N'dbo.CustomerSystemAccounts',N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.CustomerSystemAccounts))
 OR (OBJECT_ID(N'dbo.CustomerRepresentatives',N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.CustomerRepresentatives))
 OR (OBJECT_ID(N'dbo.CustomerProfiles',N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.CustomerProfiles))
 THROW 51002, 'Rollback refused: customer profile tables contain data; nothing dropped', 1;
IF OBJECT_ID(N'dbo.CustomerSystemAccounts',N'U') IS NOT NULL DROP TABLE dbo.CustomerSystemAccounts;
IF OBJECT_ID(N'dbo.CustomerRepresentatives',N'U') IS NOT NULL DROP TABLE dbo.CustomerRepresentatives;
IF OBJECT_ID(N'dbo.CustomerProfiles',N'U') IS NOT NULL DROP TABLE dbo.CustomerProfiles;
COMMIT;
PRINT N'016-rollback-customer-profile.sql: OK (tables dropped)';
