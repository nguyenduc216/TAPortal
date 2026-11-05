-- TINGEE-INTEGRATION — READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/020-notification-channels.sql. Expected last line: VERIFY_STATUS=PASS (NOT_APPLIED before the migration).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
DECLARE @fail int = 0;
PRINT N'=== NOTIFICATION CHANNELS VERIFY ===';
IF OBJECT_ID(N'dbo.NotificationChannels', N'U') IS NULL AND OBJECT_ID(N'dbo.CustomerNotificationRecipients', N'U') IS NULL AND OBJECT_ID(N'dbo.NotificationOutbox', N'U') IS NULL
BEGIN
    PRINT N'INFO: tables missing';
    PRINT N'VERIFY_STATUS=NOT_APPLIED';
    RETURN;
END
IF (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.NotificationChannels') AND name IN
    (N'Id',N'Scope',N'CustomerId',N'Channel',N'IsEnabled',N'Events',N'Settings',N'Secret',N'SecretVersion',N'LastTestAt',N'LastTestResult',N'CreatedAt',N'UpdatedAt',N'UpdatedBy')) = 14
    PRINT N'PASS: dbo.NotificationChannels 14 columns'; ELSE BEGIN PRINT N'FAIL: dbo.NotificationChannels columns missing'; SET @fail += 1; END
IF (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.CustomerNotificationRecipients') AND name IN
    (N'CustomerId',N'Email',N'ZaloPhone',N'TelegramChatId',N'Events',N'UpdatedAt',N'UpdatedBy')) = 7
    PRINT N'PASS: dbo.CustomerNotificationRecipients 7 columns'; ELSE BEGIN PRINT N'FAIL: dbo.CustomerNotificationRecipients columns missing'; SET @fail += 1; END
IF (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.NotificationOutbox') AND name IN
    (N'Id',N'EventKey',N'Scope',N'CustomerId',N'Channel',N'Recipient',N'Subject',N'Body',N'TemplateData',N'TransactionId',N'Status',N'Attempts',N'NextAttemptAt',N'LastError',N'CreatedAt',N'SentAt')) = 16
    PRINT N'PASS: dbo.NotificationOutbox 16 columns'; ELSE BEGIN PRINT N'FAIL: dbo.NotificationOutbox columns missing'; SET @fail += 1; END
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.NotificationChannels') AND name = N'UX_NC_Owner_Channel' AND is_unique = 1)
   AND EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.NotificationOutbox') AND name = N'UX_NO_EventKey' AND is_unique = 1)
   AND (SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.NotificationOutbox') AND name IN (N'IX_NO_Pending',N'IX_NO_Customer')) = 2
    PRINT N'PASS: indexes'; ELSE BEGIN PRINT N'FAIL: indexes missing'; SET @fail += 1; END
IF (SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id IN (OBJECT_ID(N'dbo.NotificationChannels'), OBJECT_ID(N'dbo.NotificationOutbox'))
    AND name IN (N'CK_NC_Channel',N'CK_NC_Scope',N'CK_NO_Status',N'CK_NO_Scope',N'CK_NO_Channel') AND is_disabled = 0) = 5
    PRINT N'PASS: 5 check constraints'; ELSE BEGIN PRINT N'FAIL: check constraints missing'; SET @fail += 1; END
IF (SELECT COUNT(*) FROM sys.foreign_keys WHERE name IN (N'FK_NC_Customer',N'FK_CNR_Customer',N'FK_NO_Customer',N'FK_NO_Transaction')) = 4
    PRINT N'PASS: 4 foreign keys'; ELSE BEGIN PRINT N'FAIL: foreign keys missing'; SET @fail += 1; END
PRINT N'VERIFY_STATUS=' + CASE WHEN @fail = 0 THEN N'PASS' ELSE N'FAIL' END;
