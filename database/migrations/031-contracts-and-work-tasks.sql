-- CONTRACTS-AND-TASKS - customer contracts, staff work tasks, recurring task templates (operator request 2026-11-07). MIGRATION_NOT_EXECUTED.
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, after 030.
-- TA Portal (internal): contracts per customer with attached files; tasks with one responsible owner and co-workers
-- (NEW -> IN_PROGRESS -> DONE / CANCELLED, no approval step), a checklist with completion times and a comment thread;
-- recurring templates that generate one task per period; per-employee progress reports.
--   dbo.CustomerContracts      contract per customer (dbo.Customers), owner employee (dbo.Employees)
--   dbo.CustomerContractFiles  attachments stored in the database (like dbo.CustomerLogos), 10 MB max each
--   dbo.WorkTaskTemplates      recurring task (optionally per customer/contract): MONTHLY | QUARTERLY | YEARLY
--   dbo.WorkTasks              tasks with one owner; one task per (TemplateId, PeriodKey)
--   dbo.WorkTaskMembers        co-workers of a task
--   dbo.WorkTaskChecklistItems checklist items with completion time
--   dbo.WorkTaskActivities     comments and created/status/checklist/assignment/edit events
--   dbo.NumberSequences        + TASK_CODE (prefix CV, 6 digits)
--   dbo.Permissions            + CONTRACTS.* and TASKS.* codes, granted to SYS_ADMIN
-- New tables and new rows only (marked CreatedBy = A0000031-0000-4000-8000-000000000031 where the table has CreatedBy);
-- no existing row is changed. Idempotent. Names are ASCII (sqlcmd code page).
-- Verify: database/scripts/051-verify-contracts-and-work-tasks.sql. Rollback: database/scripts/052-rollback-contracts-and-work-tasks.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
-- Equivalent required positive guard: DB_NAME() = 'TAPortalSys'. No USE statement.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.Customers', N'U') IS NULL OR OBJECT_ID(N'dbo.Employees', N'U') IS NULL OR OBJECT_ID(N'dbo.Permissions', N'U') IS NULL
   OR OBJECT_ID(N'dbo.RolePermissions', N'U') IS NULL OR OBJECT_ID(N'dbo.NumberSequences', N'U') IS NULL
 THROW 51001, 'Precondition failed: dbo.Customers, dbo.Employees, dbo.Permissions, dbo.RolePermissions or dbo.NumberSequences missing', 1;
DECLARE @SysAdmin uniqueidentifier = (SELECT TOP 1 Id FROM dbo.Roles WHERE Code = 'SYS_ADMIN' AND IsDeleted = 0);
IF @SysAdmin IS NULL
 THROW 51001, 'Precondition failed: role SYS_ADMIN missing', 1;
DECLARE @Marker uniqueidentifier = 'A0000031-0000-4000-8000-000000000031';

IF OBJECT_ID(N'dbo.CustomerContracts', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustomerContracts (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_CustomerContracts PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        CustomerId uniqueidentifier NOT NULL,
        ContractNo nvarchar(50) NOT NULL,
        Title nvarchar(250) NOT NULL,
        ServiceDescription nvarchar(1000) NULL,
        SignedDate date NULL,
        StartDate date NULL,
        EndDate date NULL,
        ContractValue decimal(18,0) NULL,
        BillingCycle varchar(20) NULL,
        Status varchar(20) NOT NULL CONSTRAINT DF_CustomerContracts_Status DEFAULT 'DRAFT',
        OwnerEmployeeId uniqueidentifier NULL,
        Note nvarchar(2000) NULL,
        IsDeleted bit NOT NULL CONSTRAINT DF_CustomerContracts_IsDeleted DEFAULT 0,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_CustomerContracts_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        UpdatedAt datetime2(3) NULL,
        UpdatedBy uniqueidentifier NULL,
        DeletedAt datetime2(3) NULL,
        DeletedBy uniqueidentifier NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT FK_CustomerContracts_Customer FOREIGN KEY (CustomerId) REFERENCES dbo.Customers(Id),
        CONSTRAINT FK_CustomerContracts_Owner FOREIGN KEY (OwnerEmployeeId) REFERENCES dbo.Employees(Id),
        CONSTRAINT CK_CustomerContracts_Status CHECK (Status IN ('DRAFT','ACTIVE','EXPIRED','TERMINATED')),
        CONSTRAINT CK_CustomerContracts_BillingCycle CHECK (BillingCycle IS NULL OR BillingCycle IN ('ONE_TIME','MONTHLY','QUARTERLY','YEARLY')),
        CONSTRAINT CK_CustomerContracts_Dates CHECK (EndDate IS NULL OR StartDate IS NULL OR EndDate >= StartDate),
        CONSTRAINT CK_CustomerContracts_Value CHECK (ContractValue IS NULL OR ContractValue >= 0)
    );
    CREATE UNIQUE INDEX UX_CustomerContracts_ContractNo ON dbo.CustomerContracts(ContractNo) WHERE IsDeleted = 0;
    CREATE INDEX IX_CustomerContracts_Customer ON dbo.CustomerContracts(CustomerId, IsDeleted) INCLUDE (Status, EndDate);
END

IF OBJECT_ID(N'dbo.CustomerContractFiles', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustomerContractFiles (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_CustomerContractFiles PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        ContractId uniqueidentifier NOT NULL,
        FileName nvarchar(255) NOT NULL,
        ContentType varchar(100) NOT NULL,
        SizeBytes int NOT NULL,
        Data varbinary(max) NOT NULL,
        IsDeleted bit NOT NULL CONSTRAINT DF_CustomerContractFiles_IsDeleted DEFAULT 0,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_CustomerContractFiles_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        DeletedAt datetime2(3) NULL,
        DeletedBy uniqueidentifier NULL,
        CONSTRAINT FK_CustomerContractFiles_Contract FOREIGN KEY (ContractId) REFERENCES dbo.CustomerContracts(Id),
        CONSTRAINT CK_CustomerContractFiles_Size CHECK (SizeBytes > 0 AND SizeBytes <= 10485760)
    );
    CREATE INDEX IX_CustomerContractFiles_Contract ON dbo.CustomerContractFiles(ContractId, IsDeleted);
END

IF OBJECT_ID(N'dbo.WorkTaskTemplates', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WorkTaskTemplates (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_WorkTaskTemplates PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        CustomerId uniqueidentifier NULL,
        ContractId uniqueidentifier NULL,
        Title nvarchar(250) NOT NULL,
        Description nvarchar(4000) NULL,
        ChecklistText nvarchar(4000) NULL,
        OwnerEmployeeId uniqueidentifier NOT NULL,
        Priority varchar(10) NOT NULL CONSTRAINT DF_WorkTaskTemplates_Priority DEFAULT 'NORMAL',
        Frequency varchar(10) NOT NULL,
        DueDay tinyint NOT NULL,
        DueMonthOffset tinyint NOT NULL CONSTRAINT DF_WorkTaskTemplates_DueMonthOffset DEFAULT 0,
        LeadDays smallint NOT NULL CONSTRAINT DF_WorkTaskTemplates_LeadDays DEFAULT 7,
        StartDate date NOT NULL,
        EndDate date NULL,
        IsActive bit NOT NULL CONSTRAINT DF_WorkTaskTemplates_IsActive DEFAULT 1,
        IsDeleted bit NOT NULL CONSTRAINT DF_WorkTaskTemplates_IsDeleted DEFAULT 0,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_WorkTaskTemplates_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        UpdatedAt datetime2(3) NULL,
        UpdatedBy uniqueidentifier NULL,
        DeletedAt datetime2(3) NULL,
        DeletedBy uniqueidentifier NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT FK_WorkTaskTemplates_Customer FOREIGN KEY (CustomerId) REFERENCES dbo.Customers(Id),
        CONSTRAINT FK_WorkTaskTemplates_Contract FOREIGN KEY (ContractId) REFERENCES dbo.CustomerContracts(Id),
        CONSTRAINT FK_WorkTaskTemplates_Owner FOREIGN KEY (OwnerEmployeeId) REFERENCES dbo.Employees(Id),
        CONSTRAINT CK_WorkTaskTemplates_Priority CHECK (Priority IN ('LOW','NORMAL','HIGH','URGENT')),
        CONSTRAINT CK_WorkTaskTemplates_Frequency CHECK (Frequency IN ('MONTHLY','QUARTERLY','YEARLY')),
        CONSTRAINT CK_WorkTaskTemplates_DueDay CHECK (DueDay BETWEEN 1 AND 31),
        CONSTRAINT CK_WorkTaskTemplates_DueMonthOffset CHECK (DueMonthOffset BETWEEN 0 AND 12),
        CONSTRAINT CK_WorkTaskTemplates_LeadDays CHECK (LeadDays BETWEEN 0 AND 120),
        CONSTRAINT CK_WorkTaskTemplates_Dates CHECK (EndDate IS NULL OR EndDate >= StartDate)
    );
    CREATE INDEX IX_WorkTaskTemplates_Active ON dbo.WorkTaskTemplates(IsActive, IsDeleted) INCLUDE (OwnerEmployeeId, Frequency);
END

-- One responsible owner (OwnerEmployeeId) plus co-workers (dbo.WorkTaskMembers). A task is visible only to its owner, its
-- members, its creator and holders of TASKS.VIEW_ALL (enforced by the application). Progress is derived from the checklist.
IF OBJECT_ID(N'dbo.WorkTasks', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WorkTasks (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_WorkTasks PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        TaskNo varchar(20) NOT NULL,
        Title nvarchar(250) NOT NULL,
        Description nvarchar(4000) NULL,
        CustomerId uniqueidentifier NULL,
        ContractId uniqueidentifier NULL,
        OwnerEmployeeId uniqueidentifier NOT NULL,
        Priority varchar(10) NOT NULL CONSTRAINT DF_WorkTasks_Priority DEFAULT 'NORMAL',
        Status varchar(20) NOT NULL CONSTRAINT DF_WorkTasks_Status DEFAULT 'NEW',
        Progress tinyint NOT NULL CONSTRAINT DF_WorkTasks_Progress DEFAULT 0,
        StartDate date NULL,
        DueDate date NULL,
        CompletedAt datetime2(3) NULL,
        LastActivityAt datetime2(3) NOT NULL CONSTRAINT DF_WorkTasks_LastActivityAt DEFAULT SYSUTCDATETIME(),
        TemplateId uniqueidentifier NULL,
        PeriodKey varchar(10) NULL,
        IsDeleted bit NOT NULL CONSTRAINT DF_WorkTasks_IsDeleted DEFAULT 0,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_WorkTasks_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        UpdatedAt datetime2(3) NULL,
        UpdatedBy uniqueidentifier NULL,
        DeletedAt datetime2(3) NULL,
        DeletedBy uniqueidentifier NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT FK_WorkTasks_Customer FOREIGN KEY (CustomerId) REFERENCES dbo.Customers(Id),
        CONSTRAINT FK_WorkTasks_Contract FOREIGN KEY (ContractId) REFERENCES dbo.CustomerContracts(Id),
        CONSTRAINT FK_WorkTasks_Owner FOREIGN KEY (OwnerEmployeeId) REFERENCES dbo.Employees(Id),
        CONSTRAINT FK_WorkTasks_Template FOREIGN KEY (TemplateId) REFERENCES dbo.WorkTaskTemplates(Id),
        CONSTRAINT CK_WorkTasks_Priority CHECK (Priority IN ('LOW','NORMAL','HIGH','URGENT')),
        CONSTRAINT CK_WorkTasks_Status CHECK (Status IN ('NEW','IN_PROGRESS','DONE','CANCELLED')),
        CONSTRAINT CK_WorkTasks_Progress CHECK (Progress BETWEEN 0 AND 100),
        CONSTRAINT CK_WorkTasks_Period CHECK ((TemplateId IS NULL AND PeriodKey IS NULL) OR (TemplateId IS NOT NULL AND PeriodKey IS NOT NULL))
    );
    CREATE UNIQUE INDEX UX_WorkTasks_TaskNo ON dbo.WorkTasks(TaskNo);
    CREATE UNIQUE INDEX UX_WorkTasks_TemplatePeriod ON dbo.WorkTasks(TemplateId, PeriodKey) WHERE TemplateId IS NOT NULL;
    CREATE INDEX IX_WorkTasks_Owner ON dbo.WorkTasks(OwnerEmployeeId, IsDeleted, Status) INCLUDE (DueDate, Progress, CompletedAt, LastActivityAt);
    CREATE INDEX IX_WorkTasks_Customer ON dbo.WorkTasks(CustomerId, IsDeleted) INCLUDE (Status, DueDate);
END

IF OBJECT_ID(N'dbo.WorkTaskMembers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WorkTaskMembers (
        TaskId uniqueidentifier NOT NULL,
        EmployeeId uniqueidentifier NOT NULL,
        IsActive bit NOT NULL CONSTRAINT DF_WorkTaskMembers_IsActive DEFAULT 1,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_WorkTaskMembers_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        UpdatedAt datetime2(3) NULL,
        UpdatedBy uniqueidentifier NULL,
        CONSTRAINT PK_WorkTaskMembers PRIMARY KEY (TaskId, EmployeeId),
        CONSTRAINT FK_WorkTaskMembers_Task FOREIGN KEY (TaskId) REFERENCES dbo.WorkTasks(Id),
        CONSTRAINT FK_WorkTaskMembers_Employee FOREIGN KEY (EmployeeId) REFERENCES dbo.Employees(Id)
    );
    CREATE INDEX IX_WorkTaskMembers_Employee ON dbo.WorkTaskMembers(EmployeeId, IsActive) INCLUDE (TaskId);
END

IF OBJECT_ID(N'dbo.WorkTaskChecklistItems', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WorkTaskChecklistItems (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_WorkTaskChecklistItems PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        TaskId uniqueidentifier NOT NULL,
        Title nvarchar(500) NOT NULL,
        SortOrder int NOT NULL CONSTRAINT DF_WorkTaskChecklistItems_SortOrder DEFAULT 0,
        IsDone bit NOT NULL CONSTRAINT DF_WorkTaskChecklistItems_IsDone DEFAULT 0,
        DoneAt datetime2(3) NULL,
        DoneBy uniqueidentifier NULL,
        IsDeleted bit NOT NULL CONSTRAINT DF_WorkTaskChecklistItems_IsDeleted DEFAULT 0,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_WorkTaskChecklistItems_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        DeletedAt datetime2(3) NULL,
        DeletedBy uniqueidentifier NULL,
        CONSTRAINT FK_WorkTaskChecklistItems_Task FOREIGN KEY (TaskId) REFERENCES dbo.WorkTasks(Id),
        CONSTRAINT CK_WorkTaskChecklistItems_Done CHECK ((IsDone = 0 AND DoneAt IS NULL) OR (IsDone = 1 AND DoneAt IS NOT NULL))
    );
    CREATE INDEX IX_WorkTaskChecklistItems_Task ON dbo.WorkTaskChecklistItems(TaskId, IsDeleted, SortOrder);
END

-- Timeline of a task: comments (the discussion thread) and system events.
IF OBJECT_ID(N'dbo.WorkTaskActivities', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WorkTaskActivities (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_WorkTaskActivities PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        TaskId uniqueidentifier NOT NULL,
        Kind varchar(20) NOT NULL,
        Body nvarchar(4000) NULL,
        StatusFrom varchar(20) NULL,
        StatusTo varchar(20) NULL,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_WorkTaskActivities_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        CONSTRAINT FK_WorkTaskActivities_Task FOREIGN KEY (TaskId) REFERENCES dbo.WorkTasks(Id),
        CONSTRAINT CK_WorkTaskActivities_Kind CHECK (Kind IN ('CREATED','COMMENT','STATUS','CHECKLIST','ASSIGN','EDIT'))
    );
    CREATE INDEX IX_WorkTaskActivities_Task ON dbo.WorkTaskActivities(TaskId, CreatedAt);
END

IF NOT EXISTS (SELECT 1 FROM dbo.NumberSequences WHERE Code = 'TASK_CODE')
    INSERT dbo.NumberSequences (Id, Code, ScopeType, ScopeId, Prefix, Suffix, CurrentValue, Padding, ResetPolicy, IsActive)
    SELECT NEWID(), 'TASK_CODE', s.ScopeType, NULL, N'CV', NULL, 0, 6, s.ResetPolicy, 1
    FROM (SELECT TOP 1 ScopeType, ResetPolicy FROM dbo.NumberSequences WHERE Code = 'EMPLOYEE_CODE') s;
IF NOT EXISTS (SELECT 1 FROM dbo.NumberSequences WHERE Code = 'TASK_CODE')
 THROW 51001, 'Precondition failed: NumberSequences EMPLOYEE_CODE missing (used as the template for TASK_CODE)', 1;

DECLARE @Catalogue TABLE (Code varchar(200) COLLATE DATABASE_DEFAULT NOT NULL PRIMARY KEY, Name nvarchar(250) NOT NULL, ModuleCode varchar(100) NOT NULL, FunctionCode varchar(100) NOT NULL, ActionCode varchar(50) NOT NULL);
INSERT @Catalogue (Code, Name, ModuleCode, FunctionCode, ActionCode) VALUES
 ('CONTRACTS.VIEW',          N'View customer contracts and their files',            'CONTRACTS', 'CONTRACTS', 'VIEW'),
 ('CONTRACTS.CREATE',        N'Create customer contracts',                          'CONTRACTS', 'CONTRACTS', 'CREATE'),
 ('CONTRACTS.UPDATE',        N'Update contracts and attach or remove files',        'CONTRACTS', 'CONTRACTS', 'UPDATE'),
 ('CONTRACTS.DELETE',        N'Soft delete contracts',                              'CONTRACTS', 'CONTRACTS', 'DELETE'),
 ('TASKS.VIEW',              N'Use My work: see and update own tasks',              'TASKS',     'TASKS',     'VIEW'),
 ('TASKS.CREATE',            N'Create tasks for oneself',                           'TASKS',     'TASKS',     'CREATE'),
 ('TASKS.ASSIGN',            N'Create tasks and assign them to other employees',    'TASKS',     'TASKS',     'ASSIGN'),
 ('TASKS.VIEW_ALL',          N'Supervise: see and act on every task',               'TASKS',     'TASKS',     'VIEW_ALL'),
 ('TASKS.TEMPLATES.MANAGE',  N'Manage recurring task templates',                    'TASKS',     'TEMPLATES', 'MANAGE'),
 ('TASKS.REPORT.VIEW',       N'View task progress reports per employee',            'TASKS',     'REPORT',    'VIEW');
INSERT dbo.Permissions (Id, Code, Name, ModuleCode, FunctionCode, ActionCode, Description, IsSystem, IsActive, CreatedBy)
SELECT NEWID(), c.Code, c.Name, c.ModuleCode, c.FunctionCode, c.ActionCode, N'TAPortal Admin (migration 031)', 1, 1, @Marker
FROM @Catalogue c
WHERE NOT EXISTS (SELECT 1 FROM dbo.Permissions p WHERE p.Code = c.Code AND p.IsDeleted = 0);
INSERT dbo.RolePermissions (RoleId, PermissionId, CreatedBy)
SELECT @SysAdmin, p.Id, @Marker
FROM @Catalogue c JOIN dbo.Permissions p ON p.Code = c.Code AND p.IsDeleted = 0
WHERE NOT EXISTS (SELECT 1 FROM dbo.RolePermissions rp WHERE rp.RoleId = @SysAdmin AND rp.PermissionId = p.Id);
COMMIT;
PRINT N'031-contracts-and-work-tasks.sql: OK';
