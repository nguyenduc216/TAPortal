-- CONTRACTS-AND-TASKS - Web push notifications of the admin portal (operator 2026-11-16): T.A Portal installs as an app
-- on phones and computers, and each staff member may allow notifications on each browser/device.
--   dbo.WebPushKeys (new, one row): the portal's VAPID key pair; the private key is DPAPI-protected by the application.
--   dbo.WebPushSubscriptions (new): one row per browser/device that allowed notifications, owned by a user.
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, after 058.
-- New tables only; no existing row is changed. Idempotent.
-- Verify: database/scripts/105-verify-web-push.sql. Rollback: database/scripts/106-rollback-web-push.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
 THROW 51001, 'Precondition failed: dbo.Users missing (migration 002)', 1;
IF OBJECT_ID(N'dbo.WebPushKeys', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WebPushKeys (
        Id tinyint NOT NULL CONSTRAINT PK_WebPushKeys PRIMARY KEY CONSTRAINT CK_WebPushKeys_Single CHECK (Id = 1),
        PublicKey varchar(100) NOT NULL,
        PrivateKey varbinary(max) NOT NULL,
        Subject nvarchar(200) NOT NULL,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_WebPushKeys_CreatedAt DEFAULT SYSUTCDATETIME()
    );
    PRINT N'dbo.WebPushKeys: created';
END
IF OBJECT_ID(N'dbo.WebPushSubscriptions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WebPushSubscriptions (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_WebPushSubscriptions PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        UserId uniqueidentifier NOT NULL,
        Endpoint varchar(800) NOT NULL,
        P256dh varchar(100) NOT NULL,
        Auth varchar(50) NOT NULL,
        Device nvarchar(100) NULL,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_WebPushSubscriptions_CreatedAt DEFAULT SYSUTCDATETIME(),
        LastSeenAt datetime2(3) NOT NULL CONSTRAINT DF_WebPushSubscriptions_LastSeenAt DEFAULT SYSUTCDATETIME(),
        LastSentAt datetime2(3) NULL,
        FailCount int NOT NULL CONSTRAINT DF_WebPushSubscriptions_FailCount DEFAULT 0,
        CONSTRAINT FK_WebPushSubscriptions_User FOREIGN KEY (UserId) REFERENCES dbo.Users(Id)
    );
    CREATE UNIQUE INDEX UX_WebPushSubscriptions_Endpoint ON dbo.WebPushSubscriptions(Endpoint);
    CREATE INDEX IX_WebPushSubscriptions_User ON dbo.WebPushSubscriptions(UserId);
    PRINT N'dbo.WebPushSubscriptions: created';
END
COMMIT TRANSACTION;
PRINT N'059-web-push: done';
