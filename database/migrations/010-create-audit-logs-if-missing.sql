-- TAPORTAL-SYSTEM-AUDIT-CHG-20261028T173600Z-62724B — AUD-25 (CONDITIONAL, operator review). MIGRATION_NOT_EXECUTED.
-- Run ONLY if database/scripts/007-verify-audit-logs.sql reports that dbo.AuditLogs does not exist.
-- Same definition as legacy migration 006 (which must NOT be rerun: it contains USE [TAPortal]).
-- Creates the table only when missing; never alters or drops an existing AuditLogs table.
-- Rollback (only if created by this script and still empty, after approval): DROP TABLE dbo.AuditLogs;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
-- Equivalent required positive guard: DB_NAME() = 'TAPortalSys'. No USE statement.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.Users','U') IS NULL
 THROW 51000, 'Audit required: dbo.Users missing', 1;
IF OBJECT_ID(N'dbo.AuditLogs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AuditLogs (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_AuditLogs PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        UserId uniqueidentifier NULL,
        Action nvarchar(100) NOT NULL,
        EntityName nvarchar(200) NULL,
        EntityId nvarchar(200) NULL,
        OldValues nvarchar(max) NULL,
        NewValues nvarchar(max) NULL,
        IpAddress varchar(64) NULL,
        UserAgent nvarchar(1000) NULL,
        CorrelationId varchar(100) NULL,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_AuditLogs_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_AuditLogs_User FOREIGN KEY (UserId) REFERENCES dbo.Users(Id)
    );
    CREATE INDEX IX_AuditLogs_User_CreatedAt ON dbo.AuditLogs(UserId, CreatedAt DESC);
    CREATE INDEX IX_AuditLogs_Entity ON dbo.AuditLogs(EntityName, EntityId, CreatedAt DESC);
    CREATE INDEX IX_AuditLogs_CorrelationId ON dbo.AuditLogs(CorrelationId) WHERE CorrelationId IS NOT NULL;
    PRINT N'dbo.AuditLogs created';
END
ELSE PRINT N'dbo.AuditLogs already exists; nothing changed';
COMMIT;
PRINT N'010-create-audit-logs-if-missing.sql: OK';
