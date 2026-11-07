-- CUSTOMER-PORTAL-FOUNDATION - sender name of the SYSTEM SMTP notification channel: "Hoa don tu dong T.A" -> "Hoa don tu dong"
-- (operator 2026-11-07: customers see only the "Hoa don tu dong" brand). MIGRATION_NOT_EXECUTED.
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, after 032.
-- Changes one value inside dbo.NotificationChannels.Settings (JSON text, written by System.Text.Json with \u escapes) of the
-- row Scope='SYSTEM', Channel='SMTP'; nothing else (secret, events, enabled flag) is touched. Customer channels keep their
-- own sender names. Operator approved the change of this existing row in chat (2026-11-07, "ban lam luon phan nay").
-- Verify: database/scripts/055-verify-system-email-sender-name.sql. Rollback: database/scripts/056-rollback-system-email-sender-name.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @old nvarchar(200) = N'"fromName":"Ho\u00E1 \u0111\u01A1n t\u1EF1 \u0111\u1ED9ng T.A"';
DECLARE @new nvarchar(200) = N'"fromName":"H\u00F3a \u0111\u01A1n t\u1EF1 \u0111\u1ED9ng"';
IF EXISTS (SELECT 1 FROM dbo.NotificationChannels WHERE Scope = 'SYSTEM' AND Channel = 'SMTP' AND CHARINDEX(@old, Settings) > 0)
    UPDATE dbo.NotificationChannels SET Settings = REPLACE(Settings, @old, @new)
    WHERE Scope = 'SYSTEM' AND Channel = 'SMTP' AND CHARINDEX(@old, Settings) > 0;
COMMIT;
PRINT N'033-system-email-sender-name.sql: OK';
