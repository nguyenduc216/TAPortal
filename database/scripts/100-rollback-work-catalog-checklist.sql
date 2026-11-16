-- CONTRACTS-AND-TASKS - rollback of migrations/056-work-catalog-checklist.sql: drops WorkTaskCatalog.ChecklistText
-- (checklist templates entered after 056 are lost; tasks keep their own checklists).
-- After it, 099-verify-work-catalog-checklist.sql must print VERIFY_STATUS=NOT_APPLIED.
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH(N'dbo.WorkTaskCatalog', N'ChecklistText') IS NOT NULL BEGIN ALTER TABLE dbo.WorkTaskCatalog DROP COLUMN ChecklistText; PRINT N'dbo.WorkTaskCatalog.ChecklistText: dropped'; END
COMMIT TRANSACTION;
PRINT N'100-rollback-work-catalog-checklist: done';
