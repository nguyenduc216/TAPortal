/* TAPortal - Common database bootstrap 005
   SQL Server 2014+ compatible; dbo only.
*/
USE [TAPortal];
GO

IF OBJECT_ID(N'dbo.DataScopes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DataScopes (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_DataScopes PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        Code varchar(50) NOT NULL,
        Name nvarchar(200) NOT NULL,
        ScopeType varchar(30) NOT NULL,
        Description nvarchar(1000) NULL,
        IsSystem bit NOT NULL CONSTRAINT DF_DataScopes_IsSystem DEFAULT 1,
        IsActive bit NOT NULL CONSTRAINT DF_DataScopes_IsActive DEFAULT 1,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_DataScopes_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_DataScopes_Type CHECK (ScopeType IN ('SELF','ASSIGNED','TEAM','BRANCH','COMPANY','CUSTOM'))
    );
    CREATE UNIQUE INDEX UX_DataScopes_Code ON dbo.DataScopes(Code);
END
GO

IF OBJECT_ID(N'dbo.RoleDataScopes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RoleDataScopes (
        RoleId uniqueidentifier NOT NULL,
        DataScopeId uniqueidentifier NOT NULL,
        FunctionCode varchar(100) NOT NULL CONSTRAINT DF_RoleDataScopes_FunctionCode DEFAULT '',
        ScopeReferenceId uniqueidentifier NULL,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_RoleDataScopes_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        CONSTRAINT PK_RoleDataScopes PRIMARY KEY(RoleId, DataScopeId, FunctionCode),
        CONSTRAINT FK_RoleDataScopes_Role FOREIGN KEY (RoleId) REFERENCES dbo.Roles(Id),
        CONSTRAINT FK_RoleDataScopes_DataScope FOREIGN KEY (DataScopeId) REFERENCES dbo.DataScopes(Id)
    );
END
GO

IF OBJECT_ID(N'dbo.UserDataScopes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserDataScopes (
        UserId uniqueidentifier NOT NULL,
        DataScopeId uniqueidentifier NOT NULL,
        FunctionCode varchar(100) NOT NULL CONSTRAINT DF_UserDataScopes_FunctionCode DEFAULT '',
        ScopeReferenceId uniqueidentifier NULL,
        Effect varchar(10) NOT NULL CONSTRAINT DF_UserDataScopes_Effect DEFAULT 'ALLOW',
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_UserDataScopes_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        CONSTRAINT PK_UserDataScopes PRIMARY KEY(UserId, DataScopeId, FunctionCode),
        CONSTRAINT CK_UserDataScopes_Effect CHECK (Effect IN ('ALLOW','DENY')),
        CONSTRAINT FK_UserDataScopes_User FOREIGN KEY (UserId) REFERENCES dbo.Users(Id),
        CONSTRAINT FK_UserDataScopes_DataScope FOREIGN KEY (DataScopeId) REFERENCES dbo.DataScopes(Id)
    );
END
GO

PRINT '005-create-data-scope.sql: OK';
GO
