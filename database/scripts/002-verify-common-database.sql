/* TAPortal - Verify common database bootstrap */
USE [TAPortal];
GO

PRINT '=== SCHEMAS ===';
SELECT name AS SchemaName
FROM sys.schemas
WHERE name IN ('auth','org','core','crm','audit')
ORDER BY name;

PRINT '=== COMMON TABLES ===';
SELECT s.name AS SchemaName, t.name AS TableName
FROM sys.tables t
JOIN sys.schemas s ON s.schema_id=t.schema_id
WHERE s.name IN ('auth','org','core','audit')
ORDER BY s.name,t.name;

PRINT '=== COUNTS ===';
SELECT 'auth.Roles' AS ObjectName, COUNT_BIG(*) AS RowCount FROM auth.Roles
UNION ALL SELECT 'auth.Permissions', COUNT_BIG(*) FROM auth.Permissions
UNION ALL SELECT 'auth.DataScopes', COUNT_BIG(*) FROM auth.DataScopes
UNION ALL SELECT 'core.Modules', COUNT_BIG(*) FROM core.Modules
UNION ALL SELECT 'core.Functions', COUNT_BIG(*) FROM core.Functions;

PRINT '=== MCP LOGIN/USER ===';
SELECT sp.name AS LoginName, sp.type_desc, sp.is_disabled
FROM sys.server_principals sp
WHERE sp.name=N'taportal_ai_reader';

SELECT dp.name AS DatabaseUser, rp.name AS DatabaseRole
FROM sys.database_role_members drm
JOIN sys.database_principals rp ON rp.principal_id=drm.role_principal_id
JOIN sys.database_principals dp ON dp.principal_id=drm.member_principal_id
WHERE dp.name=N'taportal_ai_reader';

PRINT '=== FOREIGN KEYS ===';
SELECT COUNT(*) AS ForeignKeyCount FROM sys.foreign_keys WHERE is_disabled=0;

PRINT '=== RESULT ===';
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name='auth') THROW 51000, 'Missing schema auth', 1;
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name='core') THROW 51001, 'Missing schema core', 1;
IF OBJECT_ID(N'auth.Users',N'U') IS NULL THROW 51002, 'Missing auth.Users', 1;
IF OBJECT_ID(N'org.Companies',N'U') IS NULL THROW 51003, 'Missing org.Companies', 1;
IF OBJECT_ID(N'core.Modules',N'U') IS NULL THROW 51004, 'Missing core.Modules', 1;
IF OBJECT_ID(N'audit.AuditLogs',N'U') IS NULL THROW 51005, 'Missing audit.AuditLogs', 1;
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name=N'taportal_ai_reader') THROW 51006, 'Missing taportal_ai_reader database user', 1;
PRINT 'COMMON DATABASE VERIFY: OK';
GO
