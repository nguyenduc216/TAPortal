/* TAPortal - Common database bootstrap 002
   Authentication / authorization core
   SQL Server 2014+ compatible; dbo only.
*/
USE [TAPortal];
GO

IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Users (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_Users PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        Username nvarchar(100) NOT NULL,
        NormalizedUsername nvarchar(100) NOT NULL,
        Email nvarchar(256) NULL,
        NormalizedEmail nvarchar(256) NULL,
        Phone nvarchar(50) NULL,
        DisplayName nvarchar(200) NOT NULL,
        PasswordHash nvarchar(1000) NULL,
        Status varchar(30) NOT NULL CONSTRAINT DF_Users_Status DEFAULT 'ACTIVE',
        LastLoginAt datetime2(3) NULL,
        IsActive bit NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT 1,
        IsDeleted bit NOT NULL CONSTRAINT DF_Users_IsDeleted DEFAULT 0,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_Users_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        UpdatedAt datetime2(3) NULL,
        UpdatedBy uniqueidentifier NULL,
        DeletedAt datetime2(3) NULL,
        DeletedBy uniqueidentifier NULL,
        RowVersion rowversion NOT NULL
    );
    CREATE UNIQUE INDEX UX_Users_NormalizedUsername ON dbo.Users(NormalizedUsername) WHERE IsDeleted = 0;
    CREATE UNIQUE INDEX UX_Users_NormalizedEmail ON dbo.Users(NormalizedEmail) WHERE NormalizedEmail IS NOT NULL AND IsDeleted = 0;
END
GO

IF OBJECT_ID(N'dbo.Roles', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Roles (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_Roles PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        Code varchar(100) NOT NULL,
        Name nvarchar(200) NOT NULL,
        Description nvarchar(1000) NULL,
        IsSystem bit NOT NULL CONSTRAINT DF_Roles_IsSystem DEFAULT 0,
        IsActive bit NOT NULL CONSTRAINT DF_Roles_IsActive DEFAULT 1,
        IsDeleted bit NOT NULL CONSTRAINT DF_Roles_IsDeleted DEFAULT 0,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_Roles_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        UpdatedAt datetime2(3) NULL,
        UpdatedBy uniqueidentifier NULL,
        DeletedAt datetime2(3) NULL,
        DeletedBy uniqueidentifier NULL,
        RowVersion rowversion NOT NULL
    );
    CREATE UNIQUE INDEX UX_Roles_Code ON dbo.Roles(Code) WHERE IsDeleted = 0;
END
GO

IF OBJECT_ID(N'dbo.Permissions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Permissions (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_Permissions PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        Code varchar(200) NOT NULL,
        Name nvarchar(250) NOT NULL,
        ModuleCode varchar(100) NULL,
        FunctionCode varchar(100) NULL,
        ActionCode varchar(50) NOT NULL,
        Description nvarchar(1000) NULL,
        IsSystem bit NOT NULL CONSTRAINT DF_Permissions_IsSystem DEFAULT 1,
        IsActive bit NOT NULL CONSTRAINT DF_Permissions_IsActive DEFAULT 1,
        IsDeleted bit NOT NULL CONSTRAINT DF_Permissions_IsDeleted DEFAULT 0,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_Permissions_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        UpdatedAt datetime2(3) NULL,
        UpdatedBy uniqueidentifier NULL,
        DeletedAt datetime2(3) NULL,
        DeletedBy uniqueidentifier NULL,
        RowVersion rowversion NOT NULL
    );
    CREATE UNIQUE INDEX UX_Permissions_Code ON dbo.Permissions(Code) WHERE IsDeleted = 0;
END
GO

IF OBJECT_ID(N'dbo.UserRoles', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserRoles (
        UserId uniqueidentifier NOT NULL,
        RoleId uniqueidentifier NOT NULL,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_UserRoles_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        CONSTRAINT PK_UserRoles PRIMARY KEY (UserId, RoleId),
        CONSTRAINT FK_UserRoles_User FOREIGN KEY (UserId) REFERENCES dbo.Users(Id),
        CONSTRAINT FK_UserRoles_Role FOREIGN KEY (RoleId) REFERENCES dbo.Roles(Id)
    );
END
GO

IF OBJECT_ID(N'dbo.RolePermissions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RolePermissions (
        RoleId uniqueidentifier NOT NULL,
        PermissionId uniqueidentifier NOT NULL,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_RolePermissions_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        CONSTRAINT PK_RolePermissions PRIMARY KEY (RoleId, PermissionId),
        CONSTRAINT FK_RolePermissions_Role FOREIGN KEY (RoleId) REFERENCES dbo.Roles(Id),
        CONSTRAINT FK_RolePermissions_Permission FOREIGN KEY (PermissionId) REFERENCES dbo.Permissions(Id)
    );
END
GO

IF OBJECT_ID(N'dbo.UserPermissions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserPermissions (
        UserId uniqueidentifier NOT NULL,
        PermissionId uniqueidentifier NOT NULL,
        Effect varchar(10) NOT NULL CONSTRAINT DF_UserPermissions_Effect DEFAULT 'ALLOW',
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_UserPermissions_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        CONSTRAINT PK_UserPermissions PRIMARY KEY (UserId, PermissionId),
        CONSTRAINT CK_UserPermissions_Effect CHECK (Effect IN ('ALLOW','DENY')),
        CONSTRAINT FK_UserPermissions_User FOREIGN KEY (UserId) REFERENCES dbo.Users(Id),
        CONSTRAINT FK_UserPermissions_Permission FOREIGN KEY (PermissionId) REFERENCES dbo.Permissions(Id)
    );
END
GO

PRINT '002-create-auth-core.sql: OK';
GO
