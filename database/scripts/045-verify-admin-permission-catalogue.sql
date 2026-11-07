-- ADMIN-ROLE-PERMISSIONS — READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/028-admin-permission-catalogue.sql. Expected last line: VERIFY_STATUS=PASS (NOT_APPLIED before the migration).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
DECLARE @fail int = 0;
PRINT N'=== ADMIN PERMISSION CATALOGUE VERIFY ===';
IF OBJECT_ID(N'dbo.Permissions', N'U') IS NULL OR OBJECT_ID(N'dbo.RolePermissions', N'U') IS NULL OR OBJECT_ID(N'dbo.Roles', N'U') IS NULL
BEGIN
    PRINT N'FAIL: dbo.Permissions, dbo.RolePermissions or dbo.Roles missing';
    PRINT N'VERIFY_STATUS=FAIL';
    RETURN;
END
DECLARE @SysAdmin uniqueidentifier = (SELECT TOP 1 Id FROM dbo.Roles WHERE Code = 'SYS_ADMIN' AND IsDeleted = 0);
IF @SysAdmin IS NULL
BEGIN
    PRINT N'FAIL: role SYS_ADMIN missing';
    PRINT N'VERIFY_STATUS=FAIL';
    RETURN;
END
DECLARE @Marker uniqueidentifier = 'A0000028-0000-4000-8000-000000000028';
DECLARE @Catalogue TABLE (Code varchar(200) COLLATE DATABASE_DEFAULT NOT NULL PRIMARY KEY);
INSERT @Catalogue (Code) VALUES
 ('DASHBOARD.VIEW'), ('CRM.CUSTOMERS.VIEW'), ('CRM.CUSTOMERS.CREATE'), ('CRM.CUSTOMERS.UPDATE'), ('CRM.CUSTOMERS.SECRETS.REVEAL'),
 ('CUSTOMERS.PORTAL.MANAGE'), ('PAYMENTS.RECONCILIATION.VIEW'), ('PAYMENTS.RECONCILIATION.MANAGE'), ('PACKAGES.VIEW'), ('PACKAGES.MANAGE'),
 ('PACKAGES.PURCHASES.VIEW'), ('PACKAGES.PURCHASES.MANAGE'), ('INTEGRATIONS.SANDBOX'), ('INTEGRATIONS.LOGS.VIEW'), ('NOTIFICATIONS.MANAGE'),
 ('AUTH.USERS.VIEW'), ('AUTH.USERS.CREATE'), ('AUTH.USERS.UPDATE'), ('AUTH.USERS.DELETE'), ('AUTH.ROLES.VIEW'), ('AUTH.ROLES.MANAGE'),
 ('SYSTEM.MENUS.MANAGE');
DECLARE @total int = (SELECT COUNT(1) FROM @Catalogue);
DECLARE @present int = (SELECT COUNT(DISTINCT c.Code) FROM @Catalogue c JOIN dbo.Permissions p ON p.Code = c.Code AND p.IsDeleted = 0);
DECLARE @granted int = (SELECT COUNT(DISTINCT c.Code) FROM @Catalogue c JOIN dbo.Permissions p ON p.Code = c.Code AND p.IsDeleted = 0
                        JOIN dbo.RolePermissions rp ON rp.PermissionId = p.Id AND rp.RoleId = @SysAdmin);
DECLARE @inactive int = (SELECT COUNT(1) FROM @Catalogue c JOIN dbo.Permissions p ON p.Code = c.Code AND p.IsDeleted = 0 WHERE p.IsActive = 0);
DECLARE @markerPermissions int = (SELECT COUNT(1) FROM dbo.Permissions WHERE CreatedBy = @Marker);
DECLARE @markerGrants int = (SELECT COUNT(1) FROM dbo.RolePermissions WHERE CreatedBy = @Marker);
PRINT N'INFO: catalogue codes = ' + CAST(@total AS nvarchar(10)) + N', present = ' + CAST(@present AS nvarchar(10))
    + N', granted to SYS_ADMIN = ' + CAST(@granted AS nvarchar(10));
PRINT N'INFO: rows added by 028 (marker): permissions = ' + CAST(@markerPermissions AS nvarchar(10)) + N', grants = ' + CAST(@markerGrants AS nvarchar(10));
IF (@present < @total OR @granted < @total) AND @markerPermissions = 0 AND @markerGrants = 0
BEGIN
    PRINT N'INFO: catalogue incomplete and no row of migration 028 found';
    PRINT N'VERIFY_STATUS=NOT_APPLIED';
    RETURN;
END
IF @present = @total PRINT N'PASS: every catalogue code exists in dbo.Permissions'; ELSE BEGIN PRINT N'FAIL: catalogue codes missing in dbo.Permissions'; SET @fail += 1; END
IF @granted = @total PRINT N'PASS: SYS_ADMIN holds every catalogue code'; ELSE BEGIN PRINT N'FAIL: SYS_ADMIN lacks catalogue codes'; SET @fail += 1; END
IF EXISTS (SELECT 1 FROM dbo.Permissions p WHERE p.IsDeleted = 0 GROUP BY p.Code HAVING COUNT(1) > 1)
    BEGIN PRINT N'FAIL: duplicate non-deleted permission codes'; SET @fail += 1; END
ELSE PRINT N'PASS: permission codes unique';
IF EXISTS (SELECT 1 FROM dbo.Permissions WHERE CreatedBy = @Marker AND Code NOT IN (SELECT Code FROM @Catalogue))
    BEGIN PRINT N'FAIL: marker rows outside the catalogue'; SET @fail += 1; END
ELSE PRINT N'PASS: migration rows limited to the catalogue';
IF @inactive > 0 PRINT N'WARN: ' + CAST(@inactive AS nvarchar(10)) + N' catalogue code(s) exist but IsActive=0 (not granted by roles until reactivated by the operator)';
PRINT N'VERIFY_STATUS=' + CASE WHEN @fail = 0 THEN N'PASS' ELSE N'FAIL' END;
