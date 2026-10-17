/* FEATURE_ID: TAPORTAL-CRM-CUSTOMER-360
   CHANGE_ID: TAPORTAL-20261014-CRM-PHASE1-005
   ORDER: 00c; read-only metadata only. Never returns business rows.
   Purpose: establish the confirmed RoleDataScopes/UserDataScopes and AuditLogs
   contracts before implementing authorization or assignment/share writes.
   CRM audit mapping: ModuleCode=CRM; FunctionCode is the secured CRM function;
   ActionCode is ASSIGN/CHANGE/REVOKE; EntityType is CustomerAssignment,
   CustomerBranchShare or CustomerServiceTeam; EntityId is the relation Id;
   OldValues/NewValues contain serialized before/after values. */
SET NOCOUNT ON;
IF DB_NAME()<>N'TAPortalSys' THROW 52370,'Wrong database; expected TAPortalSys.',1;

IF COL_LENGTH(N'dbo.AuditLogs',N'Action') IS NOT NULL OR COL_LENGTH(N'dbo.AuditLogs',N'EntityName') IS NOT NULL
    THROW 52371,'AuditLogs uses ActionCode/EntityType; legacy Action/EntityName contract detected.',1;
IF COL_LENGTH(N'dbo.AuditLogs',N'UserId') IS NULL OR COL_LENGTH(N'dbo.AuditLogs',N'ModuleCode') IS NULL OR COL_LENGTH(N'dbo.AuditLogs',N'FunctionCode') IS NULL OR COL_LENGTH(N'dbo.AuditLogs',N'ActionCode') IS NULL OR COL_LENGTH(N'dbo.AuditLogs',N'EntityType') IS NULL OR COL_LENGTH(N'dbo.AuditLogs',N'EntityId') IS NULL OR COL_LENGTH(N'dbo.AuditLogs',N'OldValues') IS NULL OR COL_LENGTH(N'dbo.AuditLogs',N'NewValues') IS NULL OR COL_LENGTH(N'dbo.AuditLogs',N'CreatedAt') IS NULL
    THROW 52372,'AuditLogs contract is incomplete.',1;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.AuditLogs') AND name=N'Id' AND system_type_id=127 AND is_identity=1)
    THROW 52373,'AuditLogs.Id must be bigint IDENTITY.',1;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.AuditLogs') AND name IN(N'ModuleCode',N'FunctionCode',N'ActionCode') AND system_type_id<>167)
    THROW 52374,'AuditLogs code columns must be varchar.',1;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.AuditLogs') AND name IN(N'EntityType',N'EntityId',N'OldValues',N'NewValues') AND system_type_id<>231)
    THROW 52375,'AuditLogs entity/value columns must be nvarchar.',1;

SELECT t.name AS TableName, c.column_id, c.name AS ColumnName,
       ty.name AS SqlType, c.max_length, c.is_nullable, c.is_identity,
       dc.definition AS DefaultDefinition
FROM sys.tables t
JOIN sys.columns c ON c.object_id=t.object_id
JOIN sys.types ty ON ty.user_type_id=c.user_type_id
LEFT JOIN sys.default_constraints dc ON dc.parent_object_id=c.object_id AND dc.parent_column_id=c.column_id
WHERE t.name IN (N'DataScopes',N'RoleDataScopes',N'UserDataScopes',N'AuditLogs',N'CustomerEmployeeAssignments',N'CustomerBranchShares',N'CustomerServiceTeams')
ORDER BY t.name,c.column_id;

SELECT OBJECT_NAME(fkc.parent_object_id) AS ParentTable,
       fk.name AS ForeignKeyName,
       OBJECT_NAME(fkc.referenced_object_id) AS ReferencedTable,
       COL_NAME(fkc.parent_object_id,fkc.parent_column_id) AS ParentColumn,
       COL_NAME(fkc.referenced_object_id,fkc.referenced_column_id) AS ReferencedColumn
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id=fk.object_id
WHERE OBJECT_NAME(fkc.parent_object_id) IN (N'DataScopes',N'RoleDataScopes',N'UserDataScopes',N'AuditLogs',N'CustomerEmployeeAssignments',N'CustomerBranchShares',N'CustomerServiceTeams')
ORDER BY ParentTable,ForeignKeyName;

SELECT OBJECT_NAME(parent_object_id) AS TableName,name AS ConstraintName,definition
FROM sys.check_constraints
WHERE OBJECT_NAME(parent_object_id) IN (N'DataScopes',N'RoleDataScopes',N'UserDataScopes',N'AuditLogs',N'CustomerEmployeeAssignments',N'CustomerBranchShares',N'CustomerServiceTeams')
ORDER BY TableName,ConstraintName;

SELECT ds.Code,ds.ScopeType,ds.IsActive,COUNT(rds.DataScopeId) AS RoleAssignments,COUNT(uds.DataScopeId) AS UserAssignments
FROM dbo.DataScopes ds
LEFT JOIN dbo.RoleDataScopes rds ON rds.DataScopeId=ds.Id
LEFT JOIN dbo.UserDataScopes uds ON uds.DataScopeId=ds.Id
GROUP BY ds.Code,ds.ScopeType,ds.IsActive
ORDER BY ds.Code;

SELECT N'READ_ONLY_METADATA_ONLY' AS MessagesMarker;