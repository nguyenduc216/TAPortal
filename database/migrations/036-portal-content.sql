-- CUSTOMER-PORTAL-FOUNDATION - emails and terms of service of Hoa don tu dong, edited in the Admin portal
-- (section "Hoa don tu dong": Mau email, Dieu khoan dich vu) and read by the Customer Portal. MIGRATION_NOT_EXECUTED.
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, after 035.
--   dbo.PortalContent  one edited text per code (EMAIL.ACCOUNT_CREATED, EMAIL.PASSWORD_RESET, TERMS.SERVICE). Defaults live
--                      in code (TAPortal.Shared PortalContent.cs); a row exists only once staff saved a change.
-- Permissions INVOICEAPP.* (module "Hoa don tu dong") granted to SYS_ADMIN; rows carry CreatedBy = marker A0000036-....
-- New table and inserted permission rows only; no existing row is changed. Idempotent.
-- Verify: database/scripts/061-verify-portal-content.sql. Rollback: database/scripts/062-rollback-portal-content.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.Permissions', N'U') IS NULL OR OBJECT_ID(N'dbo.RolePermissions', N'U') IS NULL OR OBJECT_ID(N'dbo.Roles', N'U') IS NULL
 THROW 51001, 'Precondition failed: dbo.Permissions, dbo.RolePermissions or dbo.Roles missing', 1;
DECLARE @SysAdmin uniqueidentifier = (SELECT TOP 1 Id FROM dbo.Roles WHERE Code = 'SYS_ADMIN' AND IsDeleted = 0);
IF @SysAdmin IS NULL
 THROW 51001, 'Precondition failed: role SYS_ADMIN missing', 1;
DECLARE @Marker uniqueidentifier = 'A0000036-0000-4000-8000-000000000036';

IF OBJECT_ID(N'dbo.PortalContent', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PortalContent (
        Code varchar(50) NOT NULL CONSTRAINT PK_PortalContent PRIMARY KEY,
        Subject nvarchar(300) NULL,
        Body nvarchar(max) NOT NULL,
        UpdatedAt datetime2(3) NOT NULL CONSTRAINT DF_PCt_UpdatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedBy uniqueidentifier NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT CK_PCt_Code CHECK (Code IN ('EMAIL.ACCOUNT_CREATED', 'EMAIL.PASSWORD_RESET', 'TERMS.SERVICE'))
    );
    PRINT N'dbo.PortalContent: created';
END ELSE PRINT N'dbo.PortalContent: exists';

DECLARE @Catalogue TABLE (Code varchar(200) COLLATE DATABASE_DEFAULT NOT NULL PRIMARY KEY, Name nvarchar(250) NOT NULL, ModuleCode varchar(100) NOT NULL, FunctionCode varchar(100) NOT NULL, ActionCode varchar(50) NOT NULL);
INSERT @Catalogue (Code, Name, ModuleCode, FunctionCode, ActionCode) VALUES
 ('INVOICEAPP.CONTENT.VIEW',  N'View Hoa don tu dong email templates and terms of service',  'INVOICEAPP', 'CONTENT', 'VIEW'),
 ('INVOICEAPP.CONTENT.EDIT',  N'Edit Hoa don tu dong email templates and terms of service',  'INVOICEAPP', 'CONTENT', 'EDIT');
INSERT dbo.Permissions (Id, Code, Name, ModuleCode, FunctionCode, ActionCode, Description, IsSystem, IsActive, CreatedBy)
SELECT NEWID(), c.Code, c.Name, c.ModuleCode, c.FunctionCode, c.ActionCode, N'TAPortal Admin (migration 036)', 1, 1, @Marker
FROM @Catalogue c
WHERE NOT EXISTS (SELECT 1 FROM dbo.Permissions p WHERE p.Code = c.Code AND p.IsDeleted = 0);
INSERT dbo.RolePermissions (RoleId, PermissionId, CreatedBy)
SELECT @SysAdmin, p.Id, @Marker
FROM @Catalogue c JOIN dbo.Permissions p ON p.Code = c.Code AND p.IsDeleted = 0
WHERE NOT EXISTS (SELECT 1 FROM dbo.RolePermissions rp WHERE rp.RoleId = @SysAdmin AND rp.PermissionId = p.Id);
COMMIT;
PRINT N'036-portal-content.sql: OK';
