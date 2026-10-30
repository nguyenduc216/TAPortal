-- TINGEE-INTEGRATION-CHG-20261029T203104Z-1FE09C — ROLLBACK of migrations/011, NOT_EXECUTED. Operator approval required.
-- Never touches dbo.CustomerContacts, which is not owned by these migrations.
-- Drops the Tingee foundation tables ONLY when every one of them is empty; otherwise aborts without changes.
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF (OBJECT_ID(N'dbo.CustomerOrderLines',N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.CustomerOrderLines))
 OR (OBJECT_ID(N'dbo.CustomerOrders',N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.CustomerOrders))
 OR (OBJECT_ID(N'dbo.CustomerBuyers',N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.CustomerBuyers))
 OR (OBJECT_ID(N'dbo.CustomerBankAccounts',N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.CustomerBankAccounts))
 OR (OBJECT_ID(N'dbo.CustomerPaymentSettings',N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.CustomerPaymentSettings))
 OR (OBJECT_ID(N'dbo.TingeeMerchants',N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.TingeeMerchants))
 OR (OBJECT_ID(N'dbo.TingeeWebhookInbox',N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.TingeeWebhookInbox))
 THROW 51002, 'Rollback refused: Tingee tables contain data; nothing dropped', 1;
IF OBJECT_ID(N'dbo.CustomerOrderLines',N'U') IS NOT NULL DROP TABLE dbo.CustomerOrderLines;
IF OBJECT_ID(N'dbo.CustomerOrders',N'U') IS NOT NULL DROP TABLE dbo.CustomerOrders;
IF OBJECT_ID(N'dbo.CustomerBuyers',N'U') IS NOT NULL DROP TABLE dbo.CustomerBuyers;
IF OBJECT_ID(N'dbo.CustomerBankAccounts',N'U') IS NOT NULL DROP TABLE dbo.CustomerBankAccounts;
IF OBJECT_ID(N'dbo.CustomerPaymentSettings',N'U') IS NOT NULL DROP TABLE dbo.CustomerPaymentSettings;
IF OBJECT_ID(N'dbo.TingeeMerchants',N'U') IS NOT NULL DROP TABLE dbo.TingeeMerchants;
IF OBJECT_ID(N'dbo.TingeeWebhookInbox',N'U') IS NOT NULL DROP TABLE dbo.TingeeWebhookInbox;
COMMIT;
PRINT N'013-rollback-tingee-foundation.sql: OK (tables dropped)';
