-- CONTRACTS-AND-TASKS - rollback of migrations/057-work-catalog-checklist-guides.sql: drops WorkTaskCatalog.ChecklistJson
-- (explanations entered after 057 are lost; the item names stay in ChecklistText).
-- After it, 101-verify-work-catalog-checklist-guides.sql must print VERIFY_STATUS=NOT_APPLIED.
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH(N'dbo.WorkTaskCatalog', N'ChecklistJson') IS NOT NULL BEGIN ALTER TABLE dbo.WorkTaskCatalog DROP COLUMN ChecklistJson; PRINT N'dbo.WorkTaskCatalog.ChecklistJson: dropped'; END
COMMIT TRANSACTION;
PRINT N'102-rollback-work-catalog-checklist-guides: done';
