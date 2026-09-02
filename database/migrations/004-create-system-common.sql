/* TAPortal - Common database bootstrap 004
   SQL Server 2014+ compatible; dbo only.
*/
USE [TAPortal];
GO

IF OBJECT_ID(N'dbo.Modules', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Modules (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_Modules PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        Code varchar(100) NOT NULL,
        Name nvarchar(200) NOT NULL,
        Description nvarchar(1000) NULL,
        SortOrder int NOT NULL CONSTRAINT DF_Modules_Sort DEFAULT 0,
        IsActive bit NOT NULL CONSTRAINT DF_Modules_IsActive DEFAULT 1,
        IsDeleted bit NOT NULL CONSTRAINT DF_Modules_IsDeleted DEFAULT 0,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_Modules_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        UpdatedAt datetime2(3) NULL,
        UpdatedBy uniqueidentifier NULL,
        DeletedAt datetime2(3) NULL,
        DeletedBy uniqueidentifier NULL,
        RowVersion rowversion NOT NULL
    );
    CREATE UNIQUE INDEX UX_Modules_Code ON dbo.Modules(Code) WHERE IsDeleted = 0;
END
GO

IF OBJECT_ID(N'dbo.Functions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Functions (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_Functions PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        ModuleId uniqueidentifier NOT NULL,
        Code varchar(100) NOT NULL,
        Name nvarchar(200) NOT NULL,
        Description nvarchar(1000) NULL,
        Route nvarchar(500) NULL,
        SortOrder int NOT NULL CONSTRAINT DF_Functions_Sort DEFAULT 0,
        IsActive bit NOT NULL CONSTRAINT DF_Functions_IsActive DEFAULT 1,
        IsDeleted bit NOT NULL CONSTRAINT DF_Functions_IsDeleted DEFAULT 0,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_Functions_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        UpdatedAt datetime2(3) NULL,
        UpdatedBy uniqueidentifier NULL,
        DeletedAt datetime2(3) NULL,
        DeletedBy uniqueidentifier NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT FK_Functions_Module FOREIGN KEY (ModuleId) REFERENCES dbo.Modules(Id)
    );
    CREATE UNIQUE INDEX UX_Functions_Module_Code ON dbo.Functions(ModuleId, Code) WHERE IsDeleted = 0;
END
GO

IF OBJECT_ID(N'dbo.Menus', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Menus (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_Menus PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        ParentId uniqueidentifier NULL,
        ModuleId uniqueidentifier NULL,
        FunctionId uniqueidentifier NULL,
        Code varchar(100) NOT NULL,
        Name nvarchar(200) NOT NULL,
        Icon nvarchar(100) NULL,
        Route nvarchar(500) NULL,
        SortOrder int NOT NULL CONSTRAINT DF_Menus_Sort DEFAULT 0,
        IsVisible bit NOT NULL CONSTRAINT DF_Menus_IsVisible DEFAULT 1,
        IsActive bit NOT NULL CONSTRAINT DF_Menus_IsActive DEFAULT 1,
        IsDeleted bit NOT NULL CONSTRAINT DF_Menus_IsDeleted DEFAULT 0,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_Menus_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        UpdatedAt datetime2(3) NULL,
        UpdatedBy uniqueidentifier NULL,
        DeletedAt datetime2(3) NULL,
        DeletedBy uniqueidentifier NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT FK_Menus_Parent FOREIGN KEY (ParentId) REFERENCES dbo.Menus(Id),
        CONSTRAINT FK_Menus_Module FOREIGN KEY (ModuleId) REFERENCES dbo.Modules(Id),
        CONSTRAINT FK_Menus_Function FOREIGN KEY (FunctionId) REFERENCES dbo.Functions(Id)
    );
    CREATE UNIQUE INDEX UX_Menus_Code ON dbo.Menus(Code) WHERE IsDeleted = 0;
END
GO

IF OBJECT_ID(N'dbo.MenuPermissions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MenuPermissions (
        MenuId uniqueidentifier NOT NULL,
        PermissionId uniqueidentifier NOT NULL,
        CONSTRAINT PK_MenuPermissions PRIMARY KEY(MenuId, PermissionId),
        CONSTRAINT FK_MenuPermissions_Menu FOREIGN KEY (MenuId) REFERENCES dbo.Menus(Id),
        CONSTRAINT FK_MenuPermissions_Permission FOREIGN KEY (PermissionId) REFERENCES dbo.Permissions(Id)
    );
END
GO

IF OBJECT_ID(N'dbo.Settings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Settings (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_Settings PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        ScopeType varchar(30) NOT NULL CONSTRAINT DF_Settings_ScopeType DEFAULT 'SYSTEM',
        ScopeId uniqueidentifier NULL,
        [Key] varchar(200) NOT NULL,
        [Value] nvarchar(max) NULL,
        ValueType varchar(30) NOT NULL CONSTRAINT DF_Settings_ValueType DEFAULT 'STRING',
        Description nvarchar(1000) NULL,
        IsEncrypted bit NOT NULL CONSTRAINT DF_Settings_IsEncrypted DEFAULT 0,
        IsActive bit NOT NULL CONSTRAINT DF_Settings_IsActive DEFAULT 1,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_Settings_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        UpdatedAt datetime2(3) NULL,
        UpdatedBy uniqueidentifier NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT CK_Settings_ScopeType CHECK (ScopeType IN ('SYSTEM','COMPANY','BRANCH','TEAM','USER'))
    );
    CREATE UNIQUE INDEX UX_Settings_Scope_Key ON dbo.Settings(ScopeType, ScopeId, [Key]);
END
GO

IF OBJECT_ID(N'dbo.NumberSequences', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.NumberSequences (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_NumberSequences PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        Code varchar(100) NOT NULL,
        ScopeType varchar(30) NOT NULL CONSTRAINT DF_NumberSequences_ScopeType DEFAULT 'SYSTEM',
        ScopeId uniqueidentifier NULL,
        Prefix nvarchar(50) NULL,
        Suffix nvarchar(50) NULL,
        CurrentValue bigint NOT NULL CONSTRAINT DF_NumberSequences_Current DEFAULT 0,
        Padding int NOT NULL CONSTRAINT DF_NumberSequences_Padding DEFAULT 6,
        ResetPolicy varchar(20) NOT NULL CONSTRAINT DF_NumberSequences_Reset DEFAULT 'NEVER',
        LastResetAt datetime2(3) NULL,
        IsActive bit NOT NULL CONSTRAINT DF_NumberSequences_IsActive DEFAULT 1,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_NumberSequences_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt datetime2(3) NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT CK_NumberSequences_ScopeType CHECK (ScopeType IN ('SYSTEM','COMPANY','BRANCH','TEAM')),
        CONSTRAINT CK_NumberSequences_Reset CHECK (ResetPolicy IN ('NEVER','DAILY','MONTHLY','YEARLY')),
        CONSTRAINT CK_NumberSequences_Padding CHECK (Padding BETWEEN 1 AND 20)
    );
    CREATE UNIQUE INDEX UX_NumberSequences_Scope_Code ON dbo.NumberSequences(ScopeType, ScopeId, Code);
END
GO

PRINT '004-create-system-common.sql: OK';
GO
