-- CONTRACTS-AND-TASKS - rollback of migrations/060-work-push-notifications.sql: drops dbo.WebPushState and
-- IX_WorkTaskActivities_CreatedAt (no task data is lost). After it, 107-verify-work-push-notifications.sql must print
-- VERIFY_STATUS=NOT_APPLIED.
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.WebPushState', N'U') IS NOT NULL BEGIN DROP TABLE dbo.WebPushState; PRINT N'dbo.WebPushState: dropped'; END
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WorkTaskActivities_CreatedAt' AND object_id = OBJECT_ID(N'dbo.WorkTaskActivities'))
BEGIN DROP INDEX IX_WorkTaskActivities_CreatedAt ON dbo.WorkTaskActivities; PRINT N'IX_WorkTaskActivities_CreatedAt: dropped'; END
COMMIT TRANSACTION;
PRINT N'108-rollback-work-push-notifications: done';
