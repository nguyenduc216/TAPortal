/* TAPortal - Verify common database bootstrap
   SQL Server 2014+ compatible; dbo only.
*/
USE [TAPortal];
GO

PRINT '=== COMMON TABLES IN DBO ===';
SELECT s.name AS SchemaName, t.name AS TableName
FROM sys.tables t
JOIN sys.schemas s ON s.schema_id=t.schema_id
WHERE s.name='dbo'
  AND t.name IN (
    'Users','Roles','Permissions','UserRoles','RolePermissions','UserPermissions',
    'Companies','Branches','Teams','UserBranches','UserTeams',
    'Modules','Functions','Menus','MenuPermissions','Settings','NumberSequences',
    'DataScopes','RoleDataScopes','UserDataScopes',
    'AuditLogs','LoginHistories','SystemLogs'
  )
ORDER BY t.name;
GO

PRINT '=== COUNTS ===';
SELECT 'dbo.Roles' AS ObjectName, COUNT_BIG(*) AS RowCount FROM dbo.Roles
UNION ALL SELECT 'dbo.Permissions', COUNT_BIG(*) FROM dbo.Permissions
UNION ALL SELECT 'dbo.DataScopes', COUNT_BIG(*) FROM dbo.DataScopes
UNION ALL SELECT 'dbo.Modules', COUNT_BIG(*) FROM dbo.Modules
UNION ALL SELECT 'dbo.Functions', COUNT_BIG(*) FROM dbo.Functions;
GO

PRINT '=== LEGACY TABLES OUTSIDE DBO (SHOULD BE 0) ===';
SELECT s.name AS SchemaName,t.name AS TableName
FROM sys.tables t
JOIN sys.schemas s ON s.schema_id=t.schema_id
WHERE s.name IN ('auth','org','core','audit')
ORDER BY s.name,t.name;
GO

PRINT '=== MCP LOGIN/USER ===';
SELECT sp.name AS LoginName, sp.type_desc, sp.is_disabled
FROM sys.server_principals sp
WHERE sp.name=N'taportal_ai_reader';

SELECT dp.name AS DatabaseUser, rp.name AS DatabaseRole
FROM sys.database_role_members drm
JOIN sys.database_principals rp ON rp.principal_id=drm.role_principal_id
JOIN sys.database_principals dp ON dp.principal_id=drm.member_principal_id
WHERE dp.name=N'taportal_ai_reader';
GO

PRINT '=== FOREIGN KEYS ===';
SELECT COUNT(*) AS ForeignKeyCount FROM sys.foreign_keys WHERE is_disabled=0;
GO

PRINT '=== RESULT ===';
IF OBJECT_ID(N'dbo.Users',N'U') IS NULL THROW 51001, 'Missing dbo.Users', 1;
IF OBJECT_ID(N'dbo.Roles',N'U') IS NULL THROW 51002, 'Missing dbo.Roles', 1;
IF OBJECT_ID(N'dbo.Permissions',N'U') IS NULL THROW 51003, 'Missing dbo.Permissions', 1;
IF OBJECT_ID(N'dbo.Companies',N'U') IS NULL THROW 51004, 'Missing dbo.Companies', 1;
IF OBJECT_ID(N'dbo.Branches',N'U') IS NULL THROW 51005, 'Missing dbo.Branches', 1;
IF OBJECT_ID(N'dbo.Teams',N'U') IS NULL THROW 51006, 'Missing dbo.Teams', 1;
IF OBJECT_ID(N'dbo.Modules',N'U') IS NULL THROW 51007, 'Missing dbo.Modules', 1;
IF OBJECT_ID(N'dbo.Functions',N'U') IS NULL THROW 51008, 'Missing dbo.Functions', 1;
IF OBJECT_ID(N'dbo.DataScopes',N'U') IS NULL THROW 51009, 'Missing dbo.DataScopes', 1;
IF OBJECT_ID(N'dbo.AuditLogs',N'U') IS NULL THROW 51010, 'Missing dbo.AuditLogs', 1;
IF EXISTS (
    SELECT 1 FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
    WHERE s.name IN ('auth','org','core','audit')
) THROW 51011, 'Legacy application tables still exist outside dbo', 1;
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name=N'taportal_ai_reader') THROW 51012, 'Missing taportal_ai_reader database user', 1;
PRINT 'COMMON DATABASE VERIFY: OK';
GO
