-- CONTRACTS-AND-TASKS - Checklist mẫu của đầu việc (operator 2026-11-16): each entry of Danh mục công việc carries a
-- checklist template (one item per line); creating a task with that name pre-fills its checklist, and items can be
-- added from the catalogue page or from a task being worked on.
-- Change: CONTRACTS-AND-TASKS-CHG-20261116T014956Z-621486.
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, after 055.
--   dbo.WorkTaskCatalog + ChecklistText nvarchar(4000) NULL.
-- New nullable column only; no existing row is changed. Idempotent.
-- Verify: database/scripts/099-verify-work-catalog-checklist.sql. Rollback: database/scripts/100-rollback-work-catalog-checklist.sql.
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.WorkTaskCatalog', N'U') IS NULL
 THROW 51001, 'Precondition failed: dbo.WorkTaskCatalog missing (migration 043)', 1;
IF COL_LENGTH(N'dbo.WorkTaskCatalog', N'ChecklistText') IS NULL
BEGIN
    ALTER TABLE dbo.WorkTaskCatalog ADD ChecklistText nvarchar(4000) NULL;
    PRINT N'dbo.WorkTaskCatalog.ChecklistText: added';
END
COMMIT TRANSACTION;
PRINT N'056-work-catalog-checklist: done';
