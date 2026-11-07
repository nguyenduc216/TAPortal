-- CUSTOMER-PORTAL-FOUNDATION - ROLLBACK of migrations/033-system-email-sender-name.sql: restores the previous sender name
-- of the SYSTEM SMTP notification channel ("Hoa don tu dong T.A"). Nothing else is touched.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @old nvarchar(200) = N'"fromName":"Ho\u00E1 \u0111\u01A1n t\u1EF1 \u0111\u1ED9ng T.A"';
DECLARE @new nvarchar(200) = N'"fromName":"H\u00F3a \u0111\u01A1n t\u1EF1 \u0111\u1ED9ng"';
UPDATE dbo.NotificationChannels SET Settings = REPLACE(Settings, @new, @old)
WHERE Scope = 'SYSTEM' AND Channel = 'SMTP' AND CHARINDEX(@new, Settings) > 0;
PRINT N'INFO: rows restored = ' + CAST(@@ROWCOUNT AS nvarchar(10));
COMMIT;
PRINT N'056-rollback-system-email-sender-name.sql: OK';
