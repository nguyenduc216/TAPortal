-- CONTRACTS-AND-TASKS - Diễn giải từng mục checklist mẫu (operator 2026-11-16, "gắn từng mục"): each item of an entry's
-- checklist template may carry an explanation (procedure, formula, example) shown with that item on every task of
-- that name. Stored as JSON [{"title":..,"guide":..}] next to ChecklistText (056, which keeps the item names for the
-- create form). SQL Server 2014 has no JSON functions: the application reads and writes it.
-- Change: CONTRACTS-AND-TASKS-CHG-20261116T014956Z-621486.
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, after 056.
--   dbo.WorkTaskCatalog + ChecklistJson nvarchar(max) NULL.
-- New nullable column only; no existing row is changed. Idempotent.
-- Verify: database/scripts/101-verify-work-catalog-checklist-guides.sql. Rollback: database/scripts/102-rollback-work-catalog-checklist-guides.sql.
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH(N'dbo.WorkTaskCatalog', N'ChecklistText') IS NULL
 THROW 51001, 'Precondition failed: dbo.WorkTaskCatalog.ChecklistText missing (migration 056)', 1;
IF COL_LENGTH(N'dbo.WorkTaskCatalog', N'ChecklistJson') IS NULL
BEGIN
    ALTER TABLE dbo.WorkTaskCatalog ADD ChecklistJson nvarchar(max) NULL;
    PRINT N'dbo.WorkTaskCatalog.ChecklistJson: added';
END
COMMIT TRANSACTION;
PRINT N'057-work-catalog-checklist-guides: done';
