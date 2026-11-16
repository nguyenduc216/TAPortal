-- CONTRACTS-AND-TASKS - Tài liệu của công việc (operator 2026-11-16): files attached to a task (images, Word, PDF, Excel),
-- each with an optional note on its content, shown in the "Tài liệu" tab of the task page next to Trao đổi và lịch sử.
-- Files are stored in the database like dbo.CustomerContractFiles (031), 10 MB max each; deletion is soft.
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, after 057.
--   dbo.WorkTaskDocuments (new table) + index on (TaskId, IsDeleted).
-- New table only; no existing row is changed. Idempotent.
-- Verify: database/scripts/103-verify-work-task-documents.sql. Rollback: database/scripts/104-rollback-work-task-documents.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.WorkTasks', N'U') IS NULL
 THROW 51001, 'Precondition failed: dbo.WorkTasks missing (migration 031)', 1;
IF OBJECT_ID(N'dbo.WorkTaskDocuments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WorkTaskDocuments (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_WorkTaskDocuments PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        TaskId uniqueidentifier NOT NULL,
        FileName nvarchar(255) NOT NULL,
        ContentType varchar(100) NOT NULL,
        SizeBytes int NOT NULL,
        Data varbinary(max) NOT NULL,
        Note nvarchar(1000) NULL,
        IsDeleted bit NOT NULL CONSTRAINT DF_WorkTaskDocuments_IsDeleted DEFAULT 0,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_WorkTaskDocuments_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        UpdatedAt datetime2(3) NULL,
        UpdatedBy uniqueidentifier NULL,
        DeletedAt datetime2(3) NULL,
        DeletedBy uniqueidentifier NULL,
        CONSTRAINT FK_WorkTaskDocuments_Task FOREIGN KEY (TaskId) REFERENCES dbo.WorkTasks(Id),
        CONSTRAINT CK_WorkTaskDocuments_Size CHECK (SizeBytes > 0 AND SizeBytes <= 10485760)
    );
    CREATE INDEX IX_WorkTaskDocuments_Task ON dbo.WorkTaskDocuments(TaskId, IsDeleted);
    PRINT N'dbo.WorkTaskDocuments: created';
END
COMMIT TRANSACTION;
PRINT N'058-work-task-documents: done';
