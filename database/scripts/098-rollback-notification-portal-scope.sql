-- TINGEE-INTEGRATION (notifications) - rollback of migrations/055-notification-portal-scope.sql: removes the PORTAL channels/outbox rows (Portal
-- settings only) and restores the original scope constraints of migration 020.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DELETE FROM dbo.NotificationOutbox WHERE Scope = 'PORTAL';
DELETE FROM dbo.NotificationChannels WHERE Scope = 'PORTAL';
IF OBJECT_ID(N'dbo.CK_NC_Scope', N'C') IS NOT NULL ALTER TABLE dbo.NotificationChannels DROP CONSTRAINT CK_NC_Scope;
ALTER TABLE dbo.NotificationChannels WITH CHECK ADD CONSTRAINT CK_NC_Scope CHECK (
    (Scope = 'SYSTEM' AND CustomerId IS NULL)
 OR (Scope = 'CUSTOMER' AND CustomerId IS NOT NULL AND Channel <> 'TELEGRAM_BOT'));
IF OBJECT_ID(N'dbo.CK_NO_Scope', N'C') IS NOT NULL ALTER TABLE dbo.NotificationOutbox DROP CONSTRAINT CK_NO_Scope;
ALTER TABLE dbo.NotificationOutbox WITH CHECK ADD CONSTRAINT CK_NO_Scope CHECK (Scope IN ('SYSTEM','CUSTOMER'));
COMMIT;
PRINT N'098-rollback-notification-portal-scope.sql: OK';
