-- CONTRACTS-AND-TASKS - rollback of migrations/059-web-push.sql: drops dbo.WebPushSubscriptions and dbo.WebPushKeys
-- (devices must allow notifications again; a new key pair is created on first use). After it,
-- 105-verify-web-push.sql must print VERIFY_STATUS=NOT_APPLIED.
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.WebPushSubscriptions', N'U') IS NOT NULL BEGIN DROP TABLE dbo.WebPushSubscriptions; PRINT N'dbo.WebPushSubscriptions: dropped'; END
IF OBJECT_ID(N'dbo.WebPushKeys', N'U') IS NOT NULL BEGIN DROP TABLE dbo.WebPushKeys; PRINT N'dbo.WebPushKeys: dropped'; END
COMMIT TRANSACTION;
PRINT N'106-rollback-web-push: done';
