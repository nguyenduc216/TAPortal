/*
FEATURE_ID: TAPORTAL-CORE-AUTH-MENU
CHANGE_ID: TAPORTAL-20261010-CORE-AUTH-MENU-001
PURPOSE: Add the database-driven FunctionAction to Permission mapping required by Phase 6A.
BUSINESS RULES:
- FunctionActions identify an existing UI and/or endpoint operation without encoding role names.
- One action may map to multiple permissions; any current effective permission grants that action.
- Authorization still applies User DENY > User ALLOW > Role ALLOW in application queries.
SAFETY / IMPORTANT:
- This script deliberately has no USE statement. The deployment connection selects the target database.
- It is additive and restart-safe; it does not DROP, TRUNCATE, or rewrite historical migrations.
- Verify DB_NAME() and the release manifest before execution. This repository change does not execute the script.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'dbo.Functions', N'U') IS NULL OR OBJECT_ID(N'dbo.Permissions', N'U') IS NULL
    THROW 52110, 'Required dbo.Functions/dbo.Permissions baseline is missing.', 1;
GO

IF OBJECT_ID(N'dbo.FunctionActions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.FunctionActions (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_FunctionActions PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        FunctionId uniqueidentifier NOT NULL,
        Code varchar(100) NOT NULL,
        Name nvarchar(200) NOT NULL,
        UiKey varchar(200) NULL,
        EndpointKey varchar(200) NULL,
        SortOrder int NOT NULL CONSTRAINT DF_FunctionActions_SortOrder DEFAULT 0,
        IsActive bit NOT NULL CONSTRAINT DF_FunctionActions_IsActive DEFAULT 1,
        IsDeleted bit NOT NULL CONSTRAINT DF_FunctionActions_IsDeleted DEFAULT 0,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_FunctionActions_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        UpdatedAt datetime2(3) NULL,
        UpdatedBy uniqueidentifier NULL,
        DeletedAt datetime2(3) NULL,
        DeletedBy uniqueidentifier NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT FK_FunctionActions_Function FOREIGN KEY (FunctionId) REFERENCES dbo.Functions(Id)
    );
    CREATE UNIQUE INDEX UX_FunctionActions_Function_Code ON dbo.FunctionActions(FunctionId, Code) WHERE IsDeleted=0;
    CREATE UNIQUE INDEX UX_FunctionActions_UiKey ON dbo.FunctionActions(UiKey) WHERE IsDeleted=0 AND UiKey IS NOT NULL;
    CREATE UNIQUE INDEX UX_FunctionActions_EndpointKey ON dbo.FunctionActions(EndpointKey) WHERE IsDeleted=0 AND EndpointKey IS NOT NULL;
END
GO

IF OBJECT_ID(N'dbo.FunctionActionPermissions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.FunctionActionPermissions (
        FunctionActionId uniqueidentifier NOT NULL,
        PermissionId uniqueidentifier NOT NULL,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_FunctionActionPermissions_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        CONSTRAINT PK_FunctionActionPermissions PRIMARY KEY(FunctionActionId, PermissionId),
        CONSTRAINT FK_FunctionActionPermissions_Action FOREIGN KEY (FunctionActionId) REFERENCES dbo.FunctionActions(Id),
        CONSTRAINT FK_FunctionActionPermissions_Permission FOREIGN KEY (PermissionId) REFERENCES dbo.Permissions(Id)
    );
END
GO

DECLARE @Mappings TABLE (
    ModuleCode varchar(100), FunctionCode varchar(100), ActionCode varchar(100),
    ActionName nvarchar(200), UiKey varchar(200), EndpointKey varchar(200), PermissionCode varchar(200), SortOrder int
);
INSERT @Mappings VALUES
('AUTH','USERS','VIEW',N'Xem người dùng','users.view','Users.Index','AUTH.USERS.VIEW',10),
('AUTH','USERS','CREATE',N'Tạo người dùng','users.create','Users.Create','AUTH.USERS.CREATE',20),
('AUTH','ROLES','VIEW',N'Xem vai trò','roles.view','Roles.Index','AUTH.ROLES.VIEW',10),
('CRM','CUSTOMERS','VIEW',N'Xem khách hàng','customers.view','Customers.Index','CRM.CUSTOMERS.VIEW',10),
('SYSTEM','MENUS','MANAGE',N'Quản lý menu','menus.manage','Menus.Index','SYSTEM.MENUS.MANAGE',10);

INSERT dbo.FunctionActions(FunctionId,Code,Name,UiKey,EndpointKey,SortOrder,IsActive)
SELECT f.Id,m.ActionCode,m.ActionName,m.UiKey,m.EndpointKey,m.SortOrder,1
FROM @Mappings m
JOIN dbo.Modules md ON md.Code=m.ModuleCode AND md.IsDeleted=0 AND md.IsActive=1
JOIN dbo.Functions f ON f.ModuleId=md.Id AND f.Code=m.FunctionCode AND f.IsDeleted=0 AND f.IsActive=1
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.FunctionActions fa
    WHERE fa.FunctionId=f.Id AND fa.Code=m.ActionCode AND fa.IsDeleted=0
);

INSERT dbo.FunctionActionPermissions(FunctionActionId,PermissionId)
SELECT fa.Id,p.Id
FROM @Mappings m
JOIN dbo.Modules md ON md.Code=m.ModuleCode AND md.IsDeleted=0 AND md.IsActive=1
JOIN dbo.Functions f ON f.ModuleId=md.Id AND f.Code=m.FunctionCode AND f.IsDeleted=0 AND f.IsActive=1
JOIN dbo.FunctionActions fa ON fa.FunctionId=f.Id AND fa.Code=m.ActionCode AND fa.IsDeleted=0
JOIN dbo.Permissions p ON p.Code=m.PermissionCode AND p.IsDeleted=0 AND p.IsActive=1
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.FunctionActionPermissions x WHERE x.FunctionActionId=fa.Id AND x.PermissionId=p.Id
);
GO

PRINT '053-core-dynamic-action-authorization.sql: OK';
GO
