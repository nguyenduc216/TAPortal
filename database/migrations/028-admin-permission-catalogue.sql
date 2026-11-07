-- ADMIN-ROLE-PERMISSIONS — Admin portal permission catalogue (operator request 2026-11-07). MIGRATION_NOT_EXECUTED.
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, after 027.
-- The Admin portal (TAPortal.Web) now grants each function by permission code (src/TAPortal.Web/Security/AdminPermissions.cs).
-- This migration:
--   1. INSERTs into dbo.Permissions every catalogue code that has no non-deleted row yet (existing rows are never updated
--      or deleted, whatever their name, module or active flag);
--   2. grants every catalogue code to the SYS_ADMIN role in dbo.RolePermissions when that grant is missing.
-- Every row it inserts carries CreatedBy = 'A0000028-0000-4000-8000-000000000028' (marker, not a user id), so the rollback
-- removes exactly these rows and nothing that existed before or was created later by the application (which writes the
-- acting user's id). Codes of other systems sharing the table (e.g. HR.*) are not touched. Idempotent.
-- Names are ASCII on purpose (sqlcmd reads the file in the server code page); the Admin UI shows the Vietnamese labels
-- from AdminPermissions.cs.
-- Verify: database/scripts/045-verify-admin-permission-catalogue.sql. Rollback: database/scripts/046-rollback-admin-permission-catalogue.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
-- Equivalent required positive guard: DB_NAME() = 'TAPortalSys'. No USE statement.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.Permissions', N'U') IS NULL OR OBJECT_ID(N'dbo.RolePermissions', N'U') IS NULL OR OBJECT_ID(N'dbo.Roles', N'U') IS NULL
 THROW 51001, 'Precondition failed: dbo.Permissions, dbo.RolePermissions or dbo.Roles missing (run 002 first)', 1;
IF COL_LENGTH(N'dbo.Permissions', N'ModuleCode') IS NULL OR COL_LENGTH(N'dbo.Permissions', N'FunctionCode') IS NULL
   OR COL_LENGTH(N'dbo.Permissions', N'Description') IS NULL OR COL_LENGTH(N'dbo.Permissions', N'CreatedBy') IS NULL
   OR COL_LENGTH(N'dbo.RolePermissions', N'CreatedBy') IS NULL
 THROW 51001, 'Precondition failed: dbo.Permissions/dbo.RolePermissions columns differ from migration 002', 1;
DECLARE @SysAdmin uniqueidentifier = (SELECT TOP 1 Id FROM dbo.Roles WHERE Code = 'SYS_ADMIN' AND IsDeleted = 0);
IF @SysAdmin IS NULL
 THROW 51001, 'Precondition failed: role SYS_ADMIN missing', 1;

DECLARE @Marker uniqueidentifier = 'A0000028-0000-4000-8000-000000000028';
DECLARE @Catalogue TABLE (Code varchar(200) COLLATE DATABASE_DEFAULT NOT NULL PRIMARY KEY, Name nvarchar(250) NOT NULL, ModuleCode varchar(100) NOT NULL, FunctionCode varchar(100) NOT NULL, ActionCode varchar(50) NOT NULL);
INSERT @Catalogue (Code, Name, ModuleCode, FunctionCode, ActionCode) VALUES
 ('DASHBOARD.VIEW',                 N'View the dashboard',                              'DASHBOARD',    'DASHBOARD',      'VIEW'),
 ('CRM.CUSTOMERS.VIEW',             N'View customers',                                  'CUSTOMERS',    'CUSTOMERS',      'VIEW'),
 ('CRM.CUSTOMERS.CREATE',           N'Create customers (incl. Excel import)',           'CUSTOMERS',    'CUSTOMERS',      'CREATE'),
 ('CRM.CUSTOMERS.UPDATE',           N'Update customers and their profile',              'CUSTOMERS',    'CUSTOMERS',      'UPDATE'),
 ('CRM.CUSTOMERS.SECRETS.REVEAL',   N'Reveal customer system account passwords',        'CUSTOMERS',    'SECRETS',        'REVEAL'),
 ('CUSTOMERS.PORTAL.MANAGE',        N'Manage customer portal accounts',                 'CUSTOMERS',    'PORTAL',         'MANAGE'),
 ('PAYMENTS.RECONCILIATION.VIEW',   N'View payment reconciliation',                     'PAYMENTS',     'RECONCILIATION', 'VIEW'),
 ('PAYMENTS.RECONCILIATION.MANAGE', N'Assign, accept and close reconciliation items',   'PAYMENTS',     'RECONCILIATION', 'MANAGE'),
 ('PACKAGES.VIEW',                  N'View service packages',                           'PACKAGES',     'PACKAGES',       'VIEW'),
 ('PACKAGES.MANAGE',                N'Create, edit, enable/disable service packages',   'PACKAGES',     'PACKAGES',       'MANAGE'),
 ('PACKAGES.PURCHASES.VIEW',        N'View package purchases',                          'PACKAGES',     'PURCHASES',      'VIEW'),
 ('PACKAGES.PURCHASES.MANAGE',      N'Create, confirm and cancel package purchases',    'PACKAGES',     'PURCHASES',      'MANAGE'),
 ('INTEGRATIONS.SANDBOX',           N'Use Tingee sandbox and invoice sandbox',          'INTEGRATIONS', 'SANDBOX',        'USE'),
 ('INTEGRATIONS.LOGS.VIEW',         N'View Tingee call logs',                           'INTEGRATIONS', 'LOGS',           'VIEW'),
 ('NOTIFICATIONS.MANAGE',           N'Manage notification settings',                    'INTEGRATIONS', 'NOTIFICATIONS',  'MANAGE'),
 ('AUTH.USERS.VIEW',                N'View users',                                      'SYSTEM',       'USERS',          'VIEW'),
 ('AUTH.USERS.CREATE',              N'Create users',                                    'SYSTEM',       'USERS',          'CREATE'),
 ('AUTH.USERS.UPDATE',              N'Update, lock and unlock users',                   'SYSTEM',       'USERS',          'UPDATE'),
 ('AUTH.USERS.DELETE',              N'Soft delete users',                               'SYSTEM',       'USERS',          'DELETE'),
 ('AUTH.ROLES.VIEW',                N'View roles and permissions',                      'SYSTEM',       'ROLES',          'VIEW'),
 ('AUTH.ROLES.MANAGE',              N'Manage roles and assign roles to users',          'SYSTEM',       'ROLES',          'MANAGE'),
 ('SYSTEM.MENUS.MANAGE',            N'View menu configuration',                         'SYSTEM',       'MENUS',          'MANAGE');

DECLARE @insertedPermissions int, @insertedGrants int;
INSERT dbo.Permissions (Id, Code, Name, ModuleCode, FunctionCode, ActionCode, Description, IsSystem, IsActive, CreatedBy)
SELECT NEWID(), c.Code, c.Name, c.ModuleCode, c.FunctionCode, c.ActionCode, N'TAPortal Admin (migration 028)', 1, 1, @Marker
FROM @Catalogue c
WHERE NOT EXISTS (SELECT 1 FROM dbo.Permissions p WHERE p.Code = c.Code AND p.IsDeleted = 0);
SET @insertedPermissions = @@ROWCOUNT;

INSERT dbo.RolePermissions (RoleId, PermissionId, CreatedBy)
SELECT @SysAdmin, p.Id, @Marker
FROM @Catalogue c JOIN dbo.Permissions p ON p.Code = c.Code AND p.IsDeleted = 0
WHERE NOT EXISTS (SELECT 1 FROM dbo.RolePermissions rp WHERE rp.RoleId = @SysAdmin AND rp.PermissionId = p.Id);
SET @insertedGrants = @@ROWCOUNT;
COMMIT;
PRINT N'INFO: catalogue permissions inserted = ' + CAST(@insertedPermissions AS nvarchar(10));
PRINT N'INFO: SYS_ADMIN grants inserted = ' + CAST(@insertedGrants AS nvarchar(10));
PRINT N'028-admin-permission-catalogue.sql: OK';
