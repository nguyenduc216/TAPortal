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
SELECT N'dbo.Roles' AS ObjectName, COUNT_BIG(*) AS [TotalRows] FROM dbo.Roles
UNION ALL
SELECT N'dbo.Permissions', COUNT_BIG(*) FROM dbo.Permissions
UNION ALL
SELECT N'dbo.DataScopes', COUNT_BIG(*) FROM dbo.DataScopes
UNION ALL
SELECT N'dbo.Modules', COUNT_BIG(*) FROM dbo.Modules
UNION ALL
SELECT N'dbo.Functions', COUNT_BIG(*) FROM dbo.Functions;
GO

PRINT '=== LEGACY TABLES OUTSIDE DBO (SHOULD BE 0) ===';
SELECT s.name AS SchemaName,t.name AS TableName
FROM sys.tables t
JOIN sys.schemas s ON s.schema_id=t.schema_id
WHERE s.name IN ('auth','org','core','audit')
ORDER BY s.name,t.name;
GO

PRINT '=== MCP LOGIN/USER ===';
SELECT sp.name AS LoginName, sp.type_desc AS LoginType, sp.is_disabled AS IsDisabled
FROM sys.server_principals sp
WHERE sp.name=N'taportal_ai_reader';

SELECT dp.name AS DatabaseUser, rp.name AS DatabaseRole
FROM sys.database_role_members drm
JOIN sys.database_principals rp ON rp.principal_id=drm.role_principal_id
JOIN sys.database_principals dp ON dp.principal_id=drm.member_principal_id
WHERE dp.name=N'taportal_ai_reader';
GO

PRINT '=== FOREIGN KEYS ===';
SELECT COUNT_BIG(*) AS [ForeignKeyCount]
FROM sys.foreign_keys
WHERE is_disabled=0;
GO

PRINT '=== RESULT ===';
DECLARE @Errors TABLE (ErrorMessage nvarchar(500));

IF OBJECT_ID(N'dbo.Users',N'U') IS NULL INSERT INTO @Errors VALUES (N'Missing dbo.Users');
IF OBJECT_ID(N'dbo.Roles',N'U') IS NULL INSERT INTO @Errors VALUES (N'Missing dbo.Roles');
IF OBJECT_ID(N'dbo.Permissions',N'U') IS NULL INSERT INTO @Errors VALUES (N'Missing dbo.Permissions');
IF OBJECT_ID(N'dbo.Companies',N'U') IS NULL INSERT INTO @Errors VALUES (N'Missing dbo.Companies');
IF OBJECT_ID(N'dbo.Branches',N'U') IS NULL INSERT INTO @Errors VALUES (N'Missing dbo.Branches');
IF OBJECT_ID(N'dbo.Teams',N'U') IS NULL INSERT INTO @Errors VALUES (N'Missing dbo.Teams');
IF OBJECT_ID(N'dbo.Modules',N'U') IS NULL INSERT INTO @Errors VALUES (N'Missing dbo.Modules');
IF OBJECT_ID(N'dbo.Functions',N'U') IS NULL INSERT INTO @Errors VALUES (N'Missing dbo.Functions');
IF OBJECT_ID(N'dbo.DataScopes',N'U') IS NULL INSERT INTO @Errors VALUES (N'Missing dbo.DataScopes');
IF OBJECT_ID(N'dbo.AuditLogs',N'U') IS NULL INSERT INTO @Errors VALUES (N'Missing dbo.AuditLogs');
IF EXISTS (
    SELECT 1
    FROM sys.tables t
    JOIN sys.schemas s ON s.schema_id=t.schema_id
    WHERE s.name IN ('auth','org','core','audit')
) INSERT INTO @Errors VALUES (N'Legacy application tables still exist outside dbo');
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name=N'taportal_ai_reader')
    INSERT INTO @Errors VALUES (N'Missing taportal_ai_reader database user');

IF EXISTS (SELECT 1 FROM @Errors)
BEGIN
    SELECT ErrorMessage FROM @Errors;
    RAISERROR('COMMON DATABASE VERIFY: FAILED - see ErrorMessage result set.',16,1);
END
ELSE
BEGIN
    PRINT 'COMMON DATABASE VERIFY: OK';
END
GO
