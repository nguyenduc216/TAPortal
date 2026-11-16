-- CONTRACTS-AND-TASKS - rollback of migrations/065-service-periods.sql: drops the quantities, the service months and the
-- service task lists, and the new WorkTasks columns (tasks created for a service month stay as ordinary tasks).
-- After it, 117-verify-service-periods.sql must print VERIFY_STATUS=NOT_APPLIED.
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.WorkTaskQuantities', N'U') IS NOT NULL BEGIN DROP TABLE dbo.WorkTaskQuantities; PRINT N'dbo.WorkTaskQuantities: dropped'; END
IF OBJECT_ID(N'dbo.FK_WorkTasks_ServicePeriod', N'F') IS NOT NULL BEGIN ALTER TABLE dbo.WorkTasks DROP CONSTRAINT FK_WorkTasks_ServicePeriod; PRINT N'FK_WorkTasks_ServicePeriod: dropped'; END
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WorkTasks_ServicePeriod' AND object_id = OBJECT_ID(N'dbo.WorkTasks')) DROP INDEX IX_WorkTasks_ServicePeriod ON dbo.WorkTasks;
IF COL_LENGTH(N'dbo.WorkTasks', N'ServicePeriodId') IS NOT NULL
BEGIN
    EXEC (N'ALTER TABLE dbo.WorkTasks DROP COLUMN ServicePeriodId, ServiceSortOrder, Unit, MetricCode;');
    PRINT N'dbo.WorkTasks: columns dropped';
END
IF OBJECT_ID(N'dbo.ServicePeriods', N'U') IS NOT NULL BEGIN DROP TABLE dbo.ServicePeriods; PRINT N'dbo.ServicePeriods: dropped'; END
IF OBJECT_ID(N'dbo.ServiceTasks', N'U') IS NOT NULL BEGIN DROP TABLE dbo.ServiceTasks; PRINT N'dbo.ServiceTasks: dropped'; END
COMMIT TRANSACTION;
PRINT N'118-rollback-service-periods: done';
