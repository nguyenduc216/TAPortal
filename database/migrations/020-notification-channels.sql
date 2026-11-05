-- TINGEE-INTEGRATION — notification channels (operator decision 2026-11-05). MIGRATION_NOT_EXECUTED.
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, after 019.
-- dbo.NotificationChannels: how a message is sent.
--   Scope SYSTEM (CustomerId NULL): T.A's own Zalo OA / Telegram bot / Telegram group / SMTP, set in T.A Admin, used to
--   notify the companies that use the e-invoice portal (and T.A's own staff group).
--   Scope CUSTOMER: a company's own Zalo OA / Telegram group / SMTP, set by the company in the Customer Portal, used to
--   notify that company's own buyers (and its staff group). A company never sees or uses another company's or T.A's channel.
--   Settings = non-secret JSON (host, port, sender, chat id, template id ...); Secret = tokens/passwords encrypted with
--   Windows DPAPI (LocalMachine) by the web apps — never readable from SQL alone, never shown again in a page.
-- dbo.CustomerNotificationRecipients: where T.A's system channels reach a company (email, Zalo phone, Telegram chat id).
-- dbo.NotificationOutbox: every message to send, idempotent by EventKey, retried by the webhook site's sender.
-- Creates new tables only. Idempotent. Verify: database/scripts/029-verify-notification-channels.sql.
-- Rollback: database/scripts/030-rollback-notification-channels.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
-- Equivalent required positive guard: DB_NAME() = 'TAPortalSys'. No USE statement.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.Customers', N'U') IS NULL OR OBJECT_ID(N'dbo.PaymentTransactions', N'U') IS NULL
 THROW 51001, 'Precondition failed: dbo.Customers or dbo.PaymentTransactions missing (run 019 first)', 1;

IF OBJECT_ID(N'dbo.NotificationChannels', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.NotificationChannels (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_NotificationChannels PRIMARY KEY CONSTRAINT DF_NC_Id DEFAULT NEWID(),
        Scope varchar(10) NOT NULL,
        CustomerId uniqueidentifier NULL CONSTRAINT FK_NC_Customer REFERENCES dbo.Customers(Id),
        Channel varchar(20) NOT NULL,
        IsEnabled bit NOT NULL CONSTRAINT DF_NC_Enabled DEFAULT 0,
        Events varchar(100) NOT NULL CONSTRAINT DF_NC_Events DEFAULT '',
        Settings nvarchar(4000) NOT NULL CONSTRAINT DF_NC_Settings DEFAULT N'{}',
        Secret varbinary(4000) NULL,
        SecretVersion int NOT NULL CONSTRAINT DF_NC_SecretVersion DEFAULT 0,
        LastTestAt datetime2(3) NULL,
        LastTestResult nvarchar(500) NULL,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_NC_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt datetime2(3) NULL,
        UpdatedBy uniqueidentifier NULL,
        CONSTRAINT CK_NC_Channel CHECK (Channel IN ('ZALO_OA','TELEGRAM_BOT','TELEGRAM_GROUP','SMTP')),
        CONSTRAINT CK_NC_Scope CHECK ((Scope = 'SYSTEM' AND CustomerId IS NULL)
                                   OR (Scope = 'CUSTOMER' AND CustomerId IS NOT NULL AND Channel <> 'TELEGRAM_BOT'))
    );
    CREATE UNIQUE INDEX UX_NC_Owner_Channel ON dbo.NotificationChannels(Scope, CustomerId, Channel);
    PRINT N'dbo.NotificationChannels created';
END ELSE PRINT N'dbo.NotificationChannels exists';

IF OBJECT_ID(N'dbo.CustomerNotificationRecipients', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustomerNotificationRecipients (
        CustomerId uniqueidentifier NOT NULL CONSTRAINT PK_CustomerNotificationRecipients PRIMARY KEY CONSTRAINT FK_CNR_Customer REFERENCES dbo.Customers(Id),
        Email nvarchar(256) NULL,
        ZaloPhone varchar(20) NULL,
        TelegramChatId varchar(50) NULL,
        Events varchar(100) NOT NULL CONSTRAINT DF_CNR_Events DEFAULT '',
        UpdatedAt datetime2(3) NOT NULL CONSTRAINT DF_CNR_UpdatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedBy uniqueidentifier NULL
    );
    PRINT N'dbo.CustomerNotificationRecipients created';
END ELSE PRINT N'dbo.CustomerNotificationRecipients exists';

IF OBJECT_ID(N'dbo.NotificationOutbox', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.NotificationOutbox (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_NotificationOutbox PRIMARY KEY,
        EventKey varchar(200) NOT NULL,
        Scope varchar(10) NOT NULL,
        CustomerId uniqueidentifier NULL CONSTRAINT FK_NO_Customer REFERENCES dbo.Customers(Id),
        Channel varchar(20) NOT NULL,
        Recipient nvarchar(256) NOT NULL,
        Subject nvarchar(300) NULL,
        Body nvarchar(4000) NOT NULL,
        TemplateData nvarchar(2000) NULL,
        TransactionId bigint NULL CONSTRAINT FK_NO_Transaction REFERENCES dbo.PaymentTransactions(Id),
        Status varchar(10) NOT NULL CONSTRAINT DF_NO_Status DEFAULT 'PENDING',
        Attempts int NOT NULL CONSTRAINT DF_NO_Attempts DEFAULT 0,
        NextAttemptAt datetime2(3) NOT NULL CONSTRAINT DF_NO_Next DEFAULT SYSUTCDATETIME(),
        LastError nvarchar(500) NULL,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_NO_CreatedAt DEFAULT SYSUTCDATETIME(),
        SentAt datetime2(3) NULL,
        CONSTRAINT CK_NO_Status CHECK (Status IN ('PENDING','SENT','FAILED')),
        CONSTRAINT CK_NO_Scope CHECK (Scope IN ('SYSTEM','CUSTOMER')),
        CONSTRAINT CK_NO_Channel CHECK (Channel IN ('ZALO_OA','TELEGRAM_BOT','TELEGRAM_GROUP','SMTP'))
    );
    CREATE UNIQUE INDEX UX_NO_EventKey ON dbo.NotificationOutbox(EventKey);
    CREATE INDEX IX_NO_Pending ON dbo.NotificationOutbox(Status, NextAttemptAt);
    CREATE INDEX IX_NO_Customer ON dbo.NotificationOutbox(CustomerId, CreatedAt DESC);
    PRINT N'dbo.NotificationOutbox created';
END ELSE PRINT N'dbo.NotificationOutbox exists';

COMMIT;
PRINT N'020-notification-channels.sql: OK';
