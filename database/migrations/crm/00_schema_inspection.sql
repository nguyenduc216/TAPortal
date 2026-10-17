/*
 FEATURE_ID: TAPORTAL-CRM-CUSTOMER-360
 CHANGE_ID: TAPORTAL-20261014-CRM-PHASE1-005
 ORDER: 00 (run first; read-only)
 PURPOSE: Capture metadata required to design the CRM Phase 1 migration safely.
 This script intentionally performs no DDL, DML, backup, or data-value inspection.
 SQL Server 2014 / compatibility level 120 compatible.
*/
SET NOCOUNT ON;

IF DB_NAME() <> N'TAPortalSys'
    THROW 52300, 'Wrong database; expected TAPortalSys.', 1;

SELECT DB_NAME() AS DatabaseName, CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)) AS ProductVersion,
       compatibility_level
FROM sys.databases WHERE name = DB_NAME();

DECLARE @names table (SchemaName sysname NOT NULL, ObjectName sysname NOT NULL);
INSERT @names VALUES
 (N'dbo',N'Customers'),(N'dbo',N'Companies'),(N'dbo',N'Branches'),(N'dbo',N'Departments'),(N'dbo',N'Teams'),
 (N'dbo',N'Employees'),(N'dbo',N'EmployeeAssignments'),(N'dbo',N'Users'),(N'dbo',N'Roles'),(N'dbo',N'Permissions'),
 (N'dbo',N'FunctionActions'),(N'dbo',N'FunctionActionPermissions'),(N'dbo',N'RolePermissions'),(N'dbo',N'UserPermissions'),
  (N'dbo',N'AuditLogs'),(N'dbo',N'CustomerEmployeeAssignments'),(N'dbo',N'CustomerBranchShares'),(N'dbo',N'CustomerServiceTeams');

SELECT n.SchemaName,n.ObjectName, o.type_desc,
       CASE WHEN o.object_id IS NULL THEN N'MISSING' ELSE N'PRESENT' END AS Presence
FROM @names n LEFT JOIN sys.objects o ON o.schema_id=SCHEMA_ID(n.SchemaName) AND o.name=n.ObjectName
ORDER BY n.SchemaName,n.ObjectName;

SELECT s.name AS SchemaName,o.name AS ObjectName,c.column_id,c.name AS ColumnName,
       t.name AS DataType,c.max_length,c.precision,c.scale,c.is_nullable,
       dc.definition AS DefaultDefinition
FROM sys.objects o JOIN sys.schemas s ON s.schema_id=o.schema_id
JOIN sys.columns c ON c.object_id=o.object_id JOIN sys.types t ON t.user_type_id=c.user_type_id
LEFT JOIN sys.default_constraints dc ON dc.object_id=c.default_object_id
WHERE (s.name=N'dbo' AND o.name IN (SELECT ObjectName FROM @names))
ORDER BY o.name,c.column_id;

SELECT OBJECT_SCHEMA_NAME(fkc.parent_object_id) AS ParentSchema,
       OBJECT_NAME(fkc.parent_object_id) AS ParentTable,
       COL_NAME(fkc.parent_object_id,fkc.parent_column_id) AS ParentColumn,
       OBJECT_SCHEMA_NAME(fkc.referenced_object_id) AS ReferencedSchema,
       OBJECT_NAME(fkc.referenced_object_id) AS ReferencedTable,
       COL_NAME(fkc.referenced_object_id,fkc.referenced_column_id) AS ReferencedColumn,
       fk.name AS ForeignKeyName
FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id=fk.object_id
WHERE OBJECT_NAME(fkc.parent_object_id) IN (SELECT ObjectName FROM @names)
   OR OBJECT_NAME(fkc.referenced_object_id) IN (SELECT ObjectName FROM @names)
ORDER BY ParentTable,ForeignKeyName;

SELECT OBJECT_SCHEMA_NAME(i.object_id) AS SchemaName,OBJECT_NAME(i.object_id) AS TableName,
       i.name AS IndexName,i.is_unique,i.is_primary_key,i.filter_definition,
       c.name AS ColumnName,ic.key_ordinal,ic.is_included_column
FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
WHERE OBJECT_NAME(i.object_id) IN (SELECT ObjectName FROM @names)
ORDER BY TableName,IndexName,ic.key_ordinal,ic.index_column_id;

SELECT N'NO CRM DDL WAS EXECUTED' AS InspectionStatement;