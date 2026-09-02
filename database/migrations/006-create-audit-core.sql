/* TAPortal - Common database bootstrap 006
   Audit / login / system logs
*/
USE [TAPortal];
GO

IF OBJECT_ID(N'audit.AuditLogs', N'U') IS NULL
BEGIN
    CREATE TABLE audit.AuditLogs (
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
        CONSTRAINT FK_AuditLogs_User FOREIGN KEY (UserId) REFERENCES auth.Users(Id) ON DELETE NO ACTION
    );
    CREATE INDEX IX_AuditLogs_User_CreatedAt ON audit.AuditLogs(UserId, CreatedAt DESC);
    CREATE INDEX IX_AuditLogs_Entity ON audit.AuditLogs(EntityName, EntityId, CreatedAt DESC);
    CREATE INDEX IX_AuditLogs_CorrelationId ON audit.AuditLogs(CorrelationId) WHERE CorrelationId IS NOT NULL;
END
GO

IF OBJECT_ID(N'audit.LoginHistories', N'U') IS NULL
BEGIN
    CREATE TABLE audit.LoginHistories (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_LoginHistories PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        UserId uniqueidentifier NULL,
        Username nvarchar(100) NULL,
        LoginAt datetime2(3) NOT NULL CONSTRAINT DF_LoginHistories_LoginAt DEFAULT SYSUTCDATETIME(),
        LogoutAt datetime2(3) NULL,
        IsSuccess bit NOT NULL,
        FailureReason nvarchar(1000) NULL,
        IpAddress varchar(64) NULL,
        UserAgent nvarchar(1000) NULL,
        SessionId varchar(200) NULL,
        CONSTRAINT FK_LoginHistories_User FOREIGN KEY (UserId) REFERENCES auth.Users(Id) ON DELETE NO ACTION
    );
    CREATE INDEX IX_LoginHistories_User_LoginAt ON audit.LoginHistories(UserId, LoginAt DESC);
    CREATE INDEX IX_LoginHistories_Username_LoginAt ON audit.LoginHistories(Username, LoginAt DESC);
END
GO

IF OBJECT_ID(N'audit.SystemLogs', N'U') IS NULL
BEGIN
    CREATE TABLE audit.SystemLogs (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_SystemLogs PRIMARY KEY,
        Level varchar(20) NOT NULL,
        Source nvarchar(200) NULL,
        EventCode varchar(100) NULL,
        Message nvarchar(max) NOT NULL,
        Exception nvarchar(max) NULL,
        Properties nvarchar(max) NULL,
        CorrelationId varchar(100) NULL,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_SystemLogs_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_SystemLogs_Level CHECK (Level IN ('TRACE','DEBUG','INFORMATION','WARNING','ERROR','CRITICAL'))
    );
    CREATE INDEX IX_SystemLogs_CreatedAt ON audit.SystemLogs(CreatedAt DESC);
    CREATE INDEX IX_SystemLogs_Level_CreatedAt ON audit.SystemLogs(Level, CreatedAt DESC);
END
GO
