-- CONTRACTS-AND-TASKS - Work catalogue: unit and fee measure (operator 2026-11-16). Each task type (Keo hoa don, Nhap sao ke,
-- Bang luong, ...) gets a unit (hoa don, lan, ...) and the code of a measure of the service tier tables (e.g. SLCT), so the
-- quantities entered day by day can be totalled each month and priced against the tiers.
--   dbo.WorkTaskCatalog + Unit nvarchar(50) NULL, MetricCode varchar(20) NULL.
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, after 061
-- (062 is a one-off data migration, not committed). Nullable columns only; no existing row is changed. Idempotent.
-- Verify: database/scripts/113-verify-work-catalog-unit-metric.sql. Rollback: database/scripts/114-rollback-work-catalog-unit-metric.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.WorkTaskCatalog', N'U') IS NULL
 THROW 51001, 'Precondition failed: dbo.WorkTaskCatalog missing (migration 043)', 1;
IF COL_LENGTH(N'dbo.WorkTaskCatalog', N'Unit') IS NULL
BEGIN
    ALTER TABLE dbo.WorkTaskCatalog ADD Unit nvarchar(50) NULL;
    PRINT N'dbo.WorkTaskCatalog.Unit: added';
END
IF COL_LENGTH(N'dbo.WorkTaskCatalog', N'MetricCode') IS NULL
BEGIN
    ALTER TABLE dbo.WorkTaskCatalog ADD MetricCode varchar(20) NULL;
    PRINT N'dbo.WorkTaskCatalog.MetricCode: added';
END
COMMIT TRANSACTION;
PRINT N'063-work-catalog-unit-metric: done';
