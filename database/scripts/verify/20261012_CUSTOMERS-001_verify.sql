/* CHANGE_ID: TAPORTAL-20261012-CUSTOMERS-ACTION-METADATA-004 — read-only review before/after the separately approved action script.
   No user data, credentials, role assignments, DDL or SQL mutations are returned/executed. */
IF DB_NAME() <> N'TAPortalSys' THROW 52250, 'Wrong database; expected TAPortalSys.', 1;
SELECT DB_NAME() AS DatabaseName;
SELECT c.name AS ColumnName,t.name AS DataType,c.max_length,c.is_nullable,dc.definition AS DefaultValue
FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
LEFT JOIN sys.default_constraints dc ON dc.object_id=c.default_object_id
WHERE c.object_id=OBJECT_ID(N'dbo.Customers') ORDER BY c.column_id;
SELECT i.name,i.is_primary_key,i.is_unique,i.filter_definition,c.name AS ColumnName
FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
WHERE i.object_id=OBJECT_ID(N'dbo.Customers');
SELECT fa.EndpointKey,fa.UiKey,fa.IsActive,fa.IsDeleted,p.Code AS PermissionCode,p.IsActive AS PermissionActive,p.IsDeleted AS PermissionDeleted
FROM dbo.FunctionActions fa
LEFT JOIN dbo.FunctionActionPermissions fap ON fap.FunctionActionId=fa.Id
LEFT JOIN dbo.Permissions p ON p.Id=fap.PermissionId
WHERE fa.EndpointKey IN ('Customers.Index','Customers.Create');
SELECT
    CASE WHEN EXISTS (
        SELECT 1
        FROM dbo.FunctionActions fa
        JOIN dbo.FunctionActionPermissions fap ON fap.FunctionActionId=fa.Id
        JOIN dbo.Permissions p ON p.Id=fap.PermissionId
        WHERE fa.EndpointKey='Customers.Create' AND fa.UiKey='customers.create'
          AND fa.IsActive=1 AND fa.IsDeleted=0
          AND p.Code='CRM.CUSTOMERS.CREATE' AND p.IsActive=1 AND p.IsDeleted=0
    ) THEN 'PASS' ELSE 'FAIL' END AS CustomersCreateMetadataStatus;
