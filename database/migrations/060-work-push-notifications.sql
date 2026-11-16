-- CONTRACTS-AND-TASKS - Push notifications for tasks (operator 2026-11-16: "Tat ca cac thong bao").
-- The portal's background worker (WorkPushWorker) reads new rows of dbo.WorkTaskActivities and sends web push to the
-- people of each task; once a day it sends due / overdue reminders. It remembers where it stopped in dbo.WebPushState.
--   dbo.WebPushState (new, one row created by the application): last activity time sent, last reminder day.
--   IX_WorkTaskActivities_CreatedAt (new index) on dbo.WorkTaskActivities(CreatedAt).
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, after 059.
-- New table and index only; no existing row is changed. Idempotent.
-- Verify: database/scripts/107-verify-work-push-notifications.sql. Rollback: database/scripts/108-rollback-work-push-notifications.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.WebPushSubscriptions', N'U') IS NULL
 THROW 51001, 'Precondition failed: dbo.WebPushSubscriptions missing (migration 059)', 1;
IF OBJECT_ID(N'dbo.WorkTaskActivities', N'U') IS NULL
 THROW 51002, 'Precondition failed: dbo.WorkTaskActivities missing (migration 031)', 1;
IF OBJECT_ID(N'dbo.WebPushState', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WebPushState (
        Id tinyint NOT NULL CONSTRAINT PK_WebPushState PRIMARY KEY CONSTRAINT CK_WebPushState_Single CHECK (Id = 1),
        LastActivityAt datetime2(3) NOT NULL,
        LastReminderDay date NULL,
        UpdatedAt datetime2(3) NOT NULL CONSTRAINT DF_WebPushState_UpdatedAt DEFAULT SYSUTCDATETIME()
    );
    PRINT N'dbo.WebPushState: created';
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WorkTaskActivities_CreatedAt' AND object_id = OBJECT_ID(N'dbo.WorkTaskActivities'))
BEGIN
    CREATE INDEX IX_WorkTaskActivities_CreatedAt ON dbo.WorkTaskActivities(CreatedAt);
    PRINT N'IX_WorkTaskActivities_CreatedAt: created';
END
COMMIT TRANSACTION;
PRINT N'060-work-push-notifications: done';
