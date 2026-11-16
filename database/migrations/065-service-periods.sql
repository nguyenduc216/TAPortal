-- CONTRACTS-AND-TASKS - Services done for customers month by month (operator 2026-11-16).
--   dbo.ServiceTasks: the task types (work catalogue entries) of a service, in order.
--   dbo.ServicePeriods: one service for one customer for one month (start, due, owner; contract link reserved, optional for now).
--     Creating one creates its tasks from the service's task list.
--   dbo.WorkTasks + ServicePeriodId (the service month a task belongs to), ServiceSortOrder, Unit, MetricCode (copied from the
--     work catalogue when the task is created: the unit of the quantities entered and the fee measure they count towards).
--   dbo.WorkTaskQuantities: quantities entered on a task day by day (history; the total is their sum).
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, after 063 (and 064 of
-- WEBSITE-TA). New tables and nullable columns only; no existing row is changed. Idempotent.
-- Verify: database/scripts/117-verify-service-periods.sql. Rollback: database/scripts/118-rollback-service-periods.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.Services', N'U') IS NULL OR OBJECT_ID(N'dbo.WorkTaskCatalog', N'U') IS NULL OR OBJECT_ID(N'dbo.WorkTasks', N'U') IS NULL
   OR COL_LENGTH(N'dbo.WorkTaskCatalog', N'MetricCode') IS NULL
 THROW 51001, 'Precondition failed: migrations 031, 043, 061 and 063 must be applied first', 1;

IF OBJECT_ID(N'dbo.ServiceTasks', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ServiceTasks (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_ServiceTasks PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        ServiceId uniqueidentifier NOT NULL,
        CatalogId uniqueidentifier NOT NULL,
        SortOrder int NOT NULL CONSTRAINT DF_ServiceTasks_SortOrder DEFAULT 0,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_ServiceTasks_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        CONSTRAINT FK_ServiceTasks_Service FOREIGN KEY (ServiceId) REFERENCES dbo.Services(Id),
        CONSTRAINT FK_ServiceTasks_Catalog FOREIGN KEY (CatalogId) REFERENCES dbo.WorkTaskCatalog(Id)
    );
    CREATE UNIQUE INDEX UX_ServiceTasks_ServiceCatalog ON dbo.ServiceTasks(ServiceId, CatalogId);
    PRINT N'dbo.ServiceTasks: created';
END

IF OBJECT_ID(N'dbo.ServicePeriods', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ServicePeriods (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_ServicePeriods PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        CustomerId uniqueidentifier NOT NULL,
        ServiceId uniqueidentifier NOT NULL,
        PeriodMonth date NOT NULL,
        StartDate date NULL,
        DueDate date NULL,
        OwnerEmployeeId uniqueidentifier NULL,
        ContractId uniqueidentifier NULL,
        Note nvarchar(1000) NULL,
        IsDeleted bit NOT NULL CONSTRAINT DF_ServicePeriods_IsDeleted DEFAULT 0,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_ServicePeriods_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        UpdatedAt datetime2(3) NULL,
        UpdatedBy uniqueidentifier NULL,
        CONSTRAINT FK_ServicePeriods_Customer FOREIGN KEY (CustomerId) REFERENCES dbo.Customers(Id),
        CONSTRAINT FK_ServicePeriods_Service FOREIGN KEY (ServiceId) REFERENCES dbo.Services(Id),
        CONSTRAINT FK_ServicePeriods_Owner FOREIGN KEY (OwnerEmployeeId) REFERENCES dbo.Employees(Id),
        CONSTRAINT FK_ServicePeriods_Contract FOREIGN KEY (ContractId) REFERENCES dbo.CustomerContracts(Id),
        CONSTRAINT CK_ServicePeriods_Month CHECK (DAY(PeriodMonth) = 1)
    );
    CREATE UNIQUE INDEX UX_ServicePeriods_CustomerServiceMonth ON dbo.ServicePeriods(CustomerId, ServiceId, PeriodMonth) WHERE IsDeleted = 0;
    CREATE INDEX IX_ServicePeriods_Month ON dbo.ServicePeriods(PeriodMonth, IsDeleted);
    PRINT N'dbo.ServicePeriods: created';
END

IF COL_LENGTH(N'dbo.WorkTasks', N'ServicePeriodId') IS NULL
BEGIN
    ALTER TABLE dbo.WorkTasks ADD ServicePeriodId uniqueidentifier NULL, ServiceSortOrder int NULL, Unit nvarchar(50) NULL, MetricCode varchar(20) NULL;
    PRINT N'dbo.WorkTasks: ServicePeriodId, ServiceSortOrder, Unit, MetricCode added';
END
IF OBJECT_ID(N'dbo.FK_WorkTasks_ServicePeriod', N'F') IS NULL
BEGIN
    EXEC (N'ALTER TABLE dbo.WorkTasks ADD CONSTRAINT FK_WorkTasks_ServicePeriod FOREIGN KEY (ServicePeriodId) REFERENCES dbo.ServicePeriods(Id);
            CREATE INDEX IX_WorkTasks_ServicePeriod ON dbo.WorkTasks(ServicePeriodId) WHERE ServicePeriodId IS NOT NULL;');
    PRINT N'FK_WorkTasks_ServicePeriod: added';
END

IF OBJECT_ID(N'dbo.WorkTaskQuantities', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WorkTaskQuantities (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_WorkTaskQuantities PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        TaskId uniqueidentifier NOT NULL,
        EntryDate date NOT NULL,
        Quantity decimal(18,2) NOT NULL,
        Note nvarchar(500) NULL,
        IsDeleted bit NOT NULL CONSTRAINT DF_WorkTaskQuantities_IsDeleted DEFAULT 0,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_WorkTaskQuantities_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        DeletedAt datetime2(3) NULL,
        DeletedBy uniqueidentifier NULL,
        CONSTRAINT FK_WorkTaskQuantities_Task FOREIGN KEY (TaskId) REFERENCES dbo.WorkTasks(Id),
        CONSTRAINT CK_WorkTaskQuantities_Quantity CHECK (Quantity <> 0 AND Quantity BETWEEN -1000000000 AND 1000000000)
    );
    CREATE INDEX IX_WorkTaskQuantities_Task ON dbo.WorkTaskQuantities(TaskId, IsDeleted);
    PRINT N'dbo.WorkTaskQuantities: created';
END
COMMIT TRANSACTION;
PRINT N'065-service-periods: done';
