-- TINGEE-INTEGRATION (notifications) - scope PORTAL for T.A Portal's own channels (operator 2026-11-15: "phân chia rõ cài đặt cho hoadontudong và
-- portal, thêm phần của portal"). The Portal email channel sends account emails to T.A staff (new password after a change)
-- with its own sender, separate from the SYSTEM channels that speak to customers as "Hóa đơn tự động".
-- Schema only: CK_NC_Scope and CK_NO_Scope also accept Scope='PORTAL' (no customer). No row is changed. MIGRATION_NOT_EXECUTED.
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, after 051.
-- Verify: database/scripts/097-verify-notification-portal-scope.sql. Rollback: database/scripts/098-rollback-notification-portal-scope.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.CK_NC_Scope', N'C') IS NOT NULL ALTER TABLE dbo.NotificationChannels DROP CONSTRAINT CK_NC_Scope;
ALTER TABLE dbo.NotificationChannels WITH CHECK ADD CONSTRAINT CK_NC_Scope CHECK (
    (Scope = 'SYSTEM' AND CustomerId IS NULL)
 OR (Scope = 'PORTAL' AND CustomerId IS NULL)
 OR (Scope = 'CUSTOMER' AND CustomerId IS NOT NULL AND Channel <> 'TELEGRAM_BOT'));
IF OBJECT_ID(N'dbo.CK_NO_Scope', N'C') IS NOT NULL ALTER TABLE dbo.NotificationOutbox DROP CONSTRAINT CK_NO_Scope;
ALTER TABLE dbo.NotificationOutbox WITH CHECK ADD CONSTRAINT CK_NO_Scope CHECK (Scope IN ('SYSTEM','CUSTOMER','PORTAL'));
COMMIT;
PRINT N'055-notification-portal-scope.sql: OK';
