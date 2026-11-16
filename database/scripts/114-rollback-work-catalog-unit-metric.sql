-- CONTRACTS-AND-TASKS - rollback of migrations/063-work-catalog-unit-metric.sql: drops WorkTaskCatalog.Unit and MetricCode
-- (units and measure codes entered after 063 are lost). After it, 113-verify-work-catalog-unit-metric.sql must print
-- VERIFY_STATUS=NOT_APPLIED.
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH(N'dbo.WorkTaskCatalog', N'Unit') IS NOT NULL BEGIN EXEC (N'ALTER TABLE dbo.WorkTaskCatalog DROP COLUMN Unit;'); PRINT N'Unit: dropped'; END
IF COL_LENGTH(N'dbo.WorkTaskCatalog', N'MetricCode') IS NOT NULL BEGIN EXEC (N'ALTER TABLE dbo.WorkTaskCatalog DROP COLUMN MetricCode;'); PRINT N'MetricCode: dropped'; END
COMMIT TRANSACTION;
PRINT N'114-rollback-work-catalog-unit-metric: done';
