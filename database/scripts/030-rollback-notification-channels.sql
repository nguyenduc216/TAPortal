-- TINGEE-INTEGRATION — ROLLBACK of migrations/020-notification-channels.sql.
-- Drops dbo.NotificationOutbox, dbo.CustomerNotificationRecipients and dbo.NotificationChannels. Refuses when channels or
-- recipients were configured (operator/customer settings): export and decide with the operator first.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.NotificationChannels', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.NotificationChannels)
    THROW 51002, 'Rollback refused: notification channels are configured; export them first', 1;
IF OBJECT_ID(N'dbo.CustomerNotificationRecipients', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.CustomerNotificationRecipients)
    THROW 51003, 'Rollback refused: notification recipients are configured; export them first', 1;
IF OBJECT_ID(N'dbo.NotificationOutbox', N'U') IS NOT NULL DROP TABLE dbo.NotificationOutbox;
IF OBJECT_ID(N'dbo.CustomerNotificationRecipients', N'U') IS NOT NULL DROP TABLE dbo.CustomerNotificationRecipients;
IF OBJECT_ID(N'dbo.NotificationChannels', N'U') IS NOT NULL DROP TABLE dbo.NotificationChannels;
COMMIT;
PRINT N'030-rollback-notification-channels.sql: OK';
