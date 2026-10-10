/*
FEATURE_ID: TAPORTAL-CORE-AUTH-MENU
CHANGE_ID: TAPORTAL-20261010-CORE-AUTH-MENU-002
PURPOSE: Read-only text verification for the TAPortalSys FunctionAction authorization contract.
SAFETY / IMPORTANT:
- Uses metadata reads and SELECT statements only; no application data is modified.
- Emits Messages via PRINT and raises one final error only when FAIL > 0.
*/
SET NOCOUNT ON;

DECLARE @Pass int=0, @Warn int=0, @Fail int=0;
DECLARE @Message nvarchar(2048);

IF DB_NAME()=N'TAPortalSys'
BEGIN PRINT '[PASS] Database = TAPortalSys'; SET @Pass+=1; END
ELSE
BEGIN SET @Message=N'[FAIL] Database = '+COALESCE(DB_NAME(),N'<NULL>')+N'; expected TAPortalSys'; PRINT @Message; SET @Fail+=1; END;

IF OBJECT_ID(N'dbo.FunctionActions',N'U') IS NOT NULL
BEGIN PRINT '[PASS] dbo.FunctionActions exists'; SET @Pass+=1; END
ELSE
BEGIN PRINT '[FAIL] dbo.FunctionActions is missing'; SET @Fail+=1; END;

IF OBJECT_ID(N'dbo.FunctionActionPermissions',N'U') IS NOT NULL
BEGIN PRINT '[PASS] dbo.FunctionActionPermissions exists'; SET @Pass+=1; END
ELSE
BEGIN PRINT '[FAIL] dbo.FunctionActionPermissions is missing'; SET @Fail+=1; END;

DECLARE @RequiredColumns TABLE(TableName sysname,ColumnName sysname);
INSERT @RequiredColumns VALUES
(N'FunctionActions',N'Id'),(N'FunctionActions',N'FunctionId'),(N'FunctionActions',N'Code'),
(N'FunctionActions',N'Name'),(N'FunctionActions',N'UiKey'),(N'FunctionActions',N'EndpointKey'),
(N'FunctionActions',N'SortOrder'),(N'FunctionActions',N'IsActive'),(N'FunctionActions',N'IsDeleted'),
(N'FunctionActionPermissions',N'FunctionActionId'),(N'FunctionActionPermissions',N'PermissionId');

DECLARE @TableName sysname,@ColumnName sysname;
DECLARE column_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT TableName,ColumnName FROM @RequiredColumns ORDER BY TableName,ColumnName;
OPEN column_cursor;
FETCH NEXT FROM column_cursor INTO @TableName,@ColumnName;
WHILE @@FETCH_STATUS=0
BEGIN
    IF COL_LENGTH(N'dbo.'+@TableName,@ColumnName) IS NOT NULL
    BEGIN SET @Message=N'[PASS] Column dbo.'+@TableName+N'.'+@ColumnName+N' exists'; PRINT @Message; SET @Pass+=1; END
    ELSE
    BEGIN SET @Message=N'[FAIL] Column dbo.'+@TableName+N'.'+@ColumnName+N' is missing'; PRINT @Message; SET @Fail+=1; END;
    FETCH NEXT FROM column_cursor INTO @TableName,@ColumnName;
END;
CLOSE column_cursor;
DEALLOCATE column_cursor;

IF EXISTS (
    SELECT 1 FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id=fk.object_id
    WHERE fkc.parent_object_id=OBJECT_ID(N'dbo.FunctionActions')
      AND COL_NAME(fkc.parent_object_id,fkc.parent_column_id)=N'FunctionId'
      AND fkc.referenced_object_id=OBJECT_ID(N'dbo.Functions')
      AND COL_NAME(fkc.referenced_object_id,fkc.referenced_column_id)=N'Id'
      AND fk.is_disabled=0 AND fk.is_not_trusted=0
)
BEGIN PRINT '[PASS] FK FunctionActions.FunctionId -> Functions.Id exists and is trusted'; SET @Pass+=1; END
ELSE
BEGIN PRINT '[FAIL] FK FunctionActions.FunctionId -> Functions.Id is missing, disabled, or untrusted'; SET @Fail+=1; END;

IF EXISTS (
    SELECT 1 FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id=fk.object_id
    WHERE fkc.parent_object_id=OBJECT_ID(N'dbo.FunctionActionPermissions')
      AND COL_NAME(fkc.parent_object_id,fkc.parent_column_id)=N'FunctionActionId'
      AND fkc.referenced_object_id=OBJECT_ID(N'dbo.FunctionActions')
      AND COL_NAME(fkc.referenced_object_id,fkc.referenced_column_id)=N'Id'
      AND fk.is_disabled=0 AND fk.is_not_trusted=0
)
BEGIN PRINT '[PASS] FK FunctionActionPermissions.FunctionActionId -> FunctionActions.Id exists and is trusted'; SET @Pass+=1; END
ELSE
BEGIN PRINT '[FAIL] FK FunctionActionPermissions.FunctionActionId -> FunctionActions.Id is missing, disabled, or untrusted'; SET @Fail+=1; END;

IF EXISTS (
    SELECT 1 FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id=fk.object_id
    WHERE fkc.parent_object_id=OBJECT_ID(N'dbo.FunctionActionPermissions')
      AND COL_NAME(fkc.parent_object_id,fkc.parent_column_id)=N'PermissionId'
      AND fkc.referenced_object_id=OBJECT_ID(N'dbo.Permissions')
      AND COL_NAME(fkc.referenced_object_id,fkc.referenced_column_id)=N'Id'
      AND fk.is_disabled=0 AND fk.is_not_trusted=0
)
BEGIN PRINT '[PASS] FK FunctionActionPermissions.PermissionId -> Permissions.Id exists and is trusted'; SET @Pass+=1; END
ELSE
BEGIN PRINT '[FAIL] FK FunctionActionPermissions.PermissionId -> Permissions.Id is missing, disabled, or untrusted'; SET @Fail+=1; END;

IF EXISTS (
    SELECT 1 FROM sys.indexes i
    WHERE i.object_id=OBJECT_ID(N'dbo.FunctionActions') AND i.is_unique=1 AND i.has_filter=1 AND i.filter_definition LIKE N'%IsDeleted%0%'
      AND (SELECT COUNT(*) FROM sys.index_columns ic WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)=2
      AND EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal=1 AND COL_NAME(ic.object_id,ic.column_id)=N'FunctionId')
      AND EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal=2 AND COL_NAME(ic.object_id,ic.column_id)=N'Code')
)
BEGIN PRINT '[PASS] Unique nondeleted FunctionActions(FunctionId,Code) index exists'; SET @Pass+=1; END
ELSE
BEGIN PRINT '[FAIL] Unique nondeleted FunctionActions(FunctionId,Code) index is missing'; SET @Fail+=1; END;

IF EXISTS (
    SELECT 1 FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
    WHERE i.object_id=OBJECT_ID(N'dbo.FunctionActions') AND i.is_unique=1 AND i.has_filter=1 AND i.filter_definition LIKE N'%IsDeleted%0%'
      AND (SELECT COUNT(*) FROM sys.index_columns kic WHERE kic.object_id=i.object_id AND kic.index_id=i.index_id AND kic.key_ordinal>0)=1
      AND COL_NAME(ic.object_id,ic.column_id)=N'UiKey' AND ic.key_ordinal=1
)
BEGIN PRINT '[PASS] Unique nondeleted FunctionActions.UiKey index exists'; SET @Pass+=1; END
ELSE
BEGIN PRINT '[FAIL] Unique nondeleted FunctionActions.UiKey index is missing'; SET @Fail+=1; END;

IF EXISTS (
    SELECT 1 FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
    WHERE i.object_id=OBJECT_ID(N'dbo.FunctionActions') AND i.is_unique=1 AND i.has_filter=1 AND i.filter_definition LIKE N'%IsDeleted%0%'
      AND (SELECT COUNT(*) FROM sys.index_columns kic WHERE kic.object_id=i.object_id AND kic.index_id=i.index_id AND kic.key_ordinal>0)=1
      AND COL_NAME(ic.object_id,ic.column_id)=N'EndpointKey' AND ic.key_ordinal=1
)
BEGIN PRINT '[PASS] Unique nondeleted FunctionActions.EndpointKey index exists'; SET @Pass+=1; END
ELSE
BEGIN PRINT '[FAIL] Unique nondeleted FunctionActions.EndpointKey index is missing'; SET @Fail+=1; END;

IF EXISTS (
    SELECT 1 FROM sys.indexes i
    WHERE i.object_id=OBJECT_ID(N'dbo.FunctionActionPermissions') AND i.is_unique=1
      AND (SELECT COUNT(*) FROM sys.index_columns ic WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)=2
      AND EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal=1 AND COL_NAME(ic.object_id,ic.column_id)=N'FunctionActionId')
      AND EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal=2 AND COL_NAME(ic.object_id,ic.column_id)=N'PermissionId')
)
BEGIN PRINT '[PASS] Unique FunctionActionPermissions(FunctionActionId,PermissionId) index exists'; SET @Pass+=1; END
ELSE
BEGIN PRINT '[FAIL] Unique FunctionActionPermissions(FunctionActionId,PermissionId) index is missing'; SET @Fail+=1; END;

DECLARE @Expected TABLE(ModuleCode varchar(100),FunctionCode varchar(100),ActionCode varchar(100),UiKey varchar(200),EndpointKey varchar(200),PermissionCode varchar(200));
INSERT @Expected VALUES
('AUTH','USERS','VIEW','users.view','Users.Index','AUTH.USERS.VIEW'),
('AUTH','USERS','CREATE','users.create','Users.Create','AUTH.USERS.CREATE'),
('AUTH','ROLES','VIEW','roles.view','Roles.Index','AUTH.ROLES.VIEW'),
('CRM','CUSTOMERS','VIEW','customers.view','Customers.Index','CRM.CUSTOMERS.VIEW'),
('SYSTEM','MENUS','MANAGE','menus.manage','Menus.Index','SYSTEM.MENUS.MANAGE');

DECLARE @SchemaReady bit=CASE WHEN OBJECT_ID(N'dbo.Modules',N'U') IS NOT NULL
    AND OBJECT_ID(N'dbo.Functions',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.Permissions',N'U') IS NOT NULL
    AND OBJECT_ID(N'dbo.FunctionActions',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.FunctionActionPermissions',N'U') IS NOT NULL
    AND COL_LENGTH(N'dbo.FunctionActions',N'UiKey') IS NOT NULL AND COL_LENGTH(N'dbo.FunctionActions',N'EndpointKey') IS NOT NULL
    AND COL_LENGTH(N'dbo.FunctionActions',N'IsActive') IS NOT NULL AND COL_LENGTH(N'dbo.FunctionActions',N'IsDeleted') IS NOT NULL
    AND COL_LENGTH(N'dbo.FunctionActionPermissions',N'FunctionActionId') IS NOT NULL AND COL_LENGTH(N'dbo.FunctionActionPermissions',N'PermissionId') IS NOT NULL
    THEN 1 ELSE 0 END;

DECLARE @ModuleCode varchar(100),@FunctionCode varchar(100),@ActionCode varchar(100),@UiKey varchar(200),@EndpointKey varchar(200),@PermissionCode varchar(200);
DECLARE @ActionCount int,@MappingCount int,@Sql nvarchar(max);
DECLARE mapping_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT ModuleCode,FunctionCode,ActionCode,UiKey,EndpointKey,PermissionCode FROM @Expected;
OPEN mapping_cursor;
FETCH NEXT FROM mapping_cursor INTO @ModuleCode,@FunctionCode,@ActionCode,@UiKey,@EndpointKey,@PermissionCode;
WHILE @@FETCH_STATUS=0
BEGIN
    SET @ActionCount=0; SET @MappingCount=0;
    IF @SchemaReady=1
    BEGIN
        SET @Sql=N'
SELECT @ActionCount=COUNT(DISTINCT fa.Id),@MappingCount=COUNT(DISTINCT fap.PermissionId)
FROM dbo.Modules md
JOIN dbo.Functions f ON f.ModuleId=md.Id AND f.Code=@FunctionCode AND f.IsDeleted=0 AND f.IsActive=1
JOIN dbo.FunctionActions fa ON fa.FunctionId=f.Id AND fa.Code=@ActionCode AND fa.UiKey=@UiKey AND fa.EndpointKey=@EndpointKey AND fa.IsDeleted=0 AND fa.IsActive=1
JOIN dbo.Permissions p ON p.Code=@PermissionCode AND p.IsDeleted=0 AND p.IsActive=1
LEFT JOIN dbo.FunctionActionPermissions fap ON fap.FunctionActionId=fa.Id AND fap.PermissionId=p.Id
WHERE md.Code=@ModuleCode AND md.IsDeleted=0 AND md.IsActive=1;';
        EXEC sys.sp_executesql @Sql,
            N'@ModuleCode varchar(100),@FunctionCode varchar(100),@ActionCode varchar(100),@UiKey varchar(200),@EndpointKey varchar(200),@PermissionCode varchar(200),@ActionCount int OUTPUT,@MappingCount int OUTPUT',
            @ModuleCode,@FunctionCode,@ActionCode,@UiKey,@EndpointKey,@PermissionCode,@ActionCount OUTPUT,@MappingCount OUTPUT;
    END;
    IF @ActionCount=1
    BEGIN SET @Message=N'[PASS] FunctionAction '+@UiKey+N' -> '+@EndpointKey+N' exists exactly once'; PRINT @Message; SET @Pass+=1; END
    ELSE
    BEGIN SET @Message=N'[FAIL] FunctionAction '+@UiKey+N' expected once; found '+CONVERT(nvarchar(20),@ActionCount); PRINT @Message; SET @Fail+=1; END;
    IF @MappingCount=1
    BEGIN SET @Message=N'[PASS] Mapping '+@UiKey+N' -> '+@PermissionCode; PRINT @Message; SET @Pass+=1; END
    ELSE
    BEGIN SET @Message=N'[FAIL] Mapping '+@UiKey+N' -> '+@PermissionCode+N' expected once; found '+CONVERT(nvarchar(20),@MappingCount); PRINT @Message; SET @Fail+=1; END;
    FETCH NEXT FROM mapping_cursor INTO @ModuleCode,@FunctionCode,@ActionCode,@UiKey,@EndpointKey,@PermissionCode;
END;
CLOSE mapping_cursor;
DEALLOCATE mapping_cursor;

DECLARE @DuplicateCount int=0,@InactiveReferenceCount int=0;
IF @SchemaReady=1
BEGIN
    SET @Sql=N'SELECT @Count=COUNT(*) FROM (SELECT UiKey FROM dbo.FunctionActions WHERE IsDeleted=0 AND IsActive=1 AND NULLIF(LTRIM(RTRIM(UiKey)),N'''') IS NOT NULL GROUP BY UiKey HAVING COUNT(*)>1) d;';
    EXEC sys.sp_executesql @Sql,N'@Count int OUTPUT',@DuplicateCount OUTPUT;
    IF @DuplicateCount=0 BEGIN PRINT '[PASS] No duplicate active UiKey'; SET @Pass+=1; END
    ELSE BEGIN SET @Message=N'[FAIL] Duplicate active UiKey groups = '+CONVERT(nvarchar(20),@DuplicateCount); PRINT @Message; SET @Fail+=1; END;
END;
ELSE BEGIN PRINT '[FAIL] Duplicate active UiKey check could not run because the schema contract is incomplete'; SET @Fail+=1; END;

SET @DuplicateCount=0;
IF @SchemaReady=1
BEGIN
    SET @Sql=N'SELECT @Count=COUNT(*) FROM (SELECT EndpointKey FROM dbo.FunctionActions WHERE IsDeleted=0 AND IsActive=1 AND NULLIF(LTRIM(RTRIM(EndpointKey)),N'''') IS NOT NULL GROUP BY EndpointKey HAVING COUNT(*)>1) d;';
    EXEC sys.sp_executesql @Sql,N'@Count int OUTPUT',@DuplicateCount OUTPUT;
    IF @DuplicateCount=0 BEGIN PRINT '[PASS] No duplicate active EndpointKey'; SET @Pass+=1; END
    ELSE BEGIN SET @Message=N'[FAIL] Duplicate active EndpointKey groups = '+CONVERT(nvarchar(20),@DuplicateCount); PRINT @Message; SET @Fail+=1; END;
END;
ELSE BEGIN PRINT '[FAIL] Duplicate active EndpointKey check could not run because the schema contract is incomplete'; SET @Fail+=1; END;

IF @SchemaReady=1
BEGIN
    SET @Sql=N'
SELECT @Count=COUNT(*)
FROM dbo.FunctionActionPermissions fap
LEFT JOIN dbo.FunctionActions fa ON fa.Id=fap.FunctionActionId
LEFT JOIN dbo.Functions f ON f.Id=fa.FunctionId
LEFT JOIN dbo.Modules md ON md.Id=f.ModuleId
LEFT JOIN dbo.Permissions p ON p.Id=fap.PermissionId
WHERE fa.Id IS NULL OR f.Id IS NULL OR md.Id IS NULL OR p.Id IS NULL
   OR fa.IsDeleted<>0 OR fa.IsActive<>1 OR f.IsDeleted<>0 OR f.IsActive<>1
   OR md.IsDeleted<>0 OR md.IsActive<>1 OR p.IsDeleted<>0 OR p.IsActive<>1;';
    EXEC sys.sp_executesql @Sql,N'@Count int OUTPUT',@InactiveReferenceCount OUTPUT;
    IF @InactiveReferenceCount=0 BEGIN PRINT '[PASS] All FunctionAction mappings reference active Permissions/Functions/Modules'; SET @Pass+=1; END
    ELSE BEGIN SET @Message=N'[FAIL] Inactive, deleted, or missing mapping references = '+CONVERT(nvarchar(20),@InactiveReferenceCount); PRINT @Message; SET @Fail+=1; END;
END;
ELSE BEGIN PRINT '[FAIL] Active mapping-reference check could not run because the schema contract is incomplete'; SET @Fail+=1; END;

PRINT '============================================================';
PRINT 'TAPortalSys AUTHORIZATION VERIFY SUMMARY';
PRINT 'PASS: '+CONVERT(varchar(20),@Pass);
PRINT 'WARN: '+CONVERT(varchar(20),@Warn);
PRINT 'FAIL: '+CONVERT(varchar(20),@Fail);
PRINT 'OVERALL STATUS: '+CASE WHEN @Fail>0 THEN 'FAIL' WHEN @Warn>0 THEN 'PASS_WITH_WARNINGS' ELSE 'PASS' END;
PRINT '============================================================';

IF @Fail>0
    RAISERROR('TAPortalSys authorization verification failed.',16,1);
