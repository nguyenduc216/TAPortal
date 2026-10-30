-- READ ONLY. Connect explicitly to the intended database; never assumes TAPortal/TAPortalSys.
-- CP10 (TAPORTAL-SYSTEM-AUDIT-CHG-20261029T052755Z-8ECA9D): guard added; listing queries unchanged.
IF DB_NAME() <> N'TAPortalSys'
BEGIN
    PRINT N'FAIL: current database is ' + DB_NAME() + N'; select TAPortalSys and rerun.';
    PRINT N'VERIFY_STATUS=FAIL';
    RETURN;
END
PRINT N'PASS: database TAPortalSys (schema listing follows in Results; review manually)';
SELECT DB_NAME() AS CurrentDatabase, @@SERVERNAME AS ServerName;
SELECT t.name AS TableName,c.name AS ColumnName,ty.name AS TypeName,c.max_length,c.is_nullable
FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id JOIN sys.types ty ON ty.user_type_id=c.user_type_id
WHERE SCHEMA_NAME(t.schema_id)='dbo' AND t.name IN ('Users','Customers','Roles','Permissions','UserRoles','RolePermissions','UserPermissions','CustomerPortalMemberships') ORDER BY t.name,c.column_id;
SELECT name,OBJECT_NAME(parent_object_id) AS TableName,OBJECT_NAME(referenced_object_id) AS ReferencedTable FROM sys.foreign_keys;
SELECT OBJECT_NAME(object_id) AS TableName,name,is_unique,filter_definition FROM sys.indexes WHERE object_id IN (OBJECT_ID('dbo.Users'),OBJECT_ID('dbo.Customers'),OBJECT_ID('dbo.CustomerPortalMemberships'));
SELECT Code,Name FROM dbo.Permissions WHERE IsDeleted=0;
SELECT Code,Name FROM dbo.Roles WHERE IsDeleted=0;
