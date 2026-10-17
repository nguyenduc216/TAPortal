/* FEATURE_ID: TAPORTAL-CRM-CUSTOMER-360
   CHANGE_ID: TAPORTAL-20261014-CRM-PHASE1-005
   ORDER: 00b (read-only supplement; no DDL/DML/data rows)
   Purpose: confirm predicates, constraints and non-sensitive RBAC codes before CRM seed/migration.
*/
SET NOCOUNT ON;
IF DB_NAME()<>N'TAPortalSys' THROW 52330,'Wrong database; expected TAPortalSys.',1;
IF CAST(SERVERPROPERTY('ProductMajorVersion') AS int) <> 12 THROW 52331,'SQL Server 12.x is required for this handoff.',1;
IF EXISTS (SELECT 1 FROM sys.databases WHERE name=DB_NAME() AND compatibility_level <> 120) THROW 52332,'Compatibility level 120 is required for this handoff.',1;

SELECT s.name AS SchemaName,o.name AS ObjectName,cc.name AS ConstraintName,
       cc.type_desc,cc.definition
FROM sys.check_constraints cc
JOIN sys.objects o ON o.object_id=cc.parent_object_id
JOIN sys.schemas s ON s.schema_id=o.schema_id
 WHERE o.name IN (N'Customers',N'DataScopes',N'RoleDataScopes',N'UserDataScopes',N'AuditLogs',N'Permissions',N'FunctionActions',N'FunctionActionPermissions',N'CustomerEmployeeAssignments',N'CustomerBranchShares',N'CustomerServiceTeams')
ORDER BY ObjectName,ConstraintName;

SELECT OBJECT_SCHEMA_NAME(i.object_id) AS SchemaName,OBJECT_NAME(i.object_id) AS TableName,
       i.name AS IndexName,i.is_unique,i.is_primary_key,i.is_unique_constraint,i.filter_definition,
       c.name AS ColumnName,ic.key_ordinal,ic.is_included_column
FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
WHERE OBJECT_NAME(i.object_id) IN (N'Customers',N'DataScopes',N'RoleDataScopes',N'UserDataScopes',N'AuditLogs',N'Permissions',N'FunctionActions',N'FunctionActionPermissions',N'CustomerEmployeeAssignments',N'CustomerBranchShares',N'CustomerServiceTeams')
ORDER BY TableName,IndexName,ic.key_ordinal,ic.index_column_id;

SELECT Code,Name,ModuleCode,FunctionCode,ActionCode,IsSystem,IsActive,IsDeleted
FROM dbo.Permissions WHERE Code LIKE 'CRM.%' OR ModuleCode='CRM' ORDER BY Code;
SELECT Code,Name,IsSystem,IsActive,IsDeleted FROM dbo.Roles WHERE Code IN ('SYS_ADMIN') OR Code LIKE '%CRM%' ORDER BY Code;
SELECT Code,Name,ScopeType,IsSystem,IsActive FROM dbo.DataScopes ORDER BY Code;
SELECT N'READ_ONLY_METADATA_ONLY' AS MessagesMarker;