/* TAPortal - Common database bootstrap 005
   Data scopes for row-level application authorization
*/
USE [TAPortal];
GO

IF OBJECT_ID(N'auth.DataScopes', N'U') IS NULL
BEGIN
    CREATE TABLE auth.DataScopes (
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
    CREATE UNIQUE INDEX UX_DataScopes_Code ON auth.DataScopes(Code);
END
GO

IF OBJECT_ID(N'auth.RoleDataScopes', N'U') IS NULL
BEGIN
    CREATE TABLE auth.RoleDataScopes (
        RoleId uniqueidentifier NOT NULL,
        DataScopeId uniqueidentifier NOT NULL,
        FunctionCode varchar(100) NULL,
        ScopeReferenceId uniqueidentifier NULL,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_RoleDataScopes_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        CONSTRAINT PK_RoleDataScopes PRIMARY KEY(RoleId, DataScopeId, FunctionCode),
        CONSTRAINT FK_RoleDataScopes_Role FOREIGN KEY (RoleId) REFERENCES auth.Roles(Id) ON DELETE NO ACTION,
        CONSTRAINT FK_RoleDataScopes_DataScope FOREIGN KEY (DataScopeId) REFERENCES auth.DataScopes(Id) ON DELETE NO ACTION
    );
END
GO

IF OBJECT_ID(N'auth.UserDataScopes', N'U') IS NULL
BEGIN
    CREATE TABLE auth.UserDataScopes (
        UserId uniqueidentifier NOT NULL,
        DataScopeId uniqueidentifier NOT NULL,
        FunctionCode varchar(100) NULL,
        ScopeReferenceId uniqueidentifier NULL,
        Effect varchar(10) NOT NULL CONSTRAINT DF_UserDataScopes_Effect DEFAULT 'ALLOW',
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_UserDataScopes_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        CONSTRAINT PK_UserDataScopes PRIMARY KEY(UserId, DataScopeId, FunctionCode),
        CONSTRAINT CK_UserDataScopes_Effect CHECK (Effect IN ('ALLOW','DENY')),
        CONSTRAINT FK_UserDataScopes_User FOREIGN KEY (UserId) REFERENCES auth.Users(Id) ON DELETE NO ACTION,
        CONSTRAINT FK_UserDataScopes_DataScope FOREIGN KEY (DataScopeId) REFERENCES auth.DataScopes(Id) ON DELETE NO ACTION
    );
END
GO
