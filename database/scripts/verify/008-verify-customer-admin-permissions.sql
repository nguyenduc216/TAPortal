/*
 FEATURE_ID: TAPORTAL-CRM-CUSTOMER-360
 CHANGE_ID: TAPORTAL-20261014-CRM-PHASE1-008-DB
 PURPOSE: Read-only verification of CRM Customers.Assign/Share/Team metadata and mappings.
 SAFETY:
 - SELECT and metadata reads only; no transaction, INSERT, UPDATE, DELETE, or DDL.
 - Messages use RAISERROR(...,10,1) WITH NOWAIT so SSMS shows every result immediately.
 - This script never grants a role or user permission. Existing administrator assignments
   are reported for manual review and are not treated as seed-created assignments.
 - SQL Server 2014 compatible; no hard-coded GUIDs.
*/
SET NOCOUNT ON;

DECLARE @Pass int=0,@Fail int=0,@Message nvarchar(2048),@Sql nvarchar(max);
DECLARE @Ready bit=0;

IF DB_NAME()=N'TAPortalSys'
    IF @Count=1 BEGIN SET @Message=N'[PASS] '+@Code+N' maps to '+@EndpointKey; RAISERROR('%s',10,1,@Message) WITH NOWAIT; SET @Pass=@Pass+1; END
ELSE
BEGIN SET @Message=N'[FAIL] Database = '+COALESCE(DB_NAME(),N'<NULL>')+N'; expected TAPortalSys'; RAISERROR('%s',10,1,@Message) WITH NOWAIT; SET @Fail=@Fail+1; END;

IF OBJECT_ID(N'dbo.Modules',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.Functions',N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.Permissions',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.FunctionActions',N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.FunctionActionPermissions',N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.FunctionActions',N'UiKey') IS NOT NULL AND COL_LENGTH(N'dbo.FunctionActions',N'EndpointKey') IS NOT NULL
   AND COL_LENGTH(N'dbo.FunctionActionPermissions',N'FunctionActionId') IS NOT NULL AND COL_LENGTH(N'dbo.FunctionActionPermissions',N'PermissionId') IS NOT NULL
BEGIN SET @Ready=1; SET @Message=N'[PASS] Authorization metadata schema is available'; RAISERROR('%s',10,1,@Message) WITH NOWAIT; SET @Pass=@Pass+1; END
ELSE
BEGIN SET @Message=N'[FAIL] Authorization metadata schema is missing or incompatible'; RAISERROR('%s',10,1,@Message) WITH NOWAIT; SET @Fail=@Fail+1; END;

DECLARE @Expected TABLE(Code varchar(200),ModuleCode varchar(100),FunctionCode varchar(100),ActionCode varchar(100),UiKey varchar(200),EndpointKey varchar(200));
INSERT @Expected VALUES
('CRM.CUSTOMERS.ASSIGN','CRM','Customers','Assign','customers.assign','Customers.Assign'),
('CRM.CUSTOMERS.SHARE','CRM','Customers','Share','customers.share','Customers.Share'),
('CRM.CUSTOMERS.TEAM','CRM','Customers','Team','customers.team','Customers.Team');

DECLARE @Code varchar(200),@ModuleCode varchar(100),@FunctionCode varchar(100),@ActionCode varchar(100),@UiKey varchar(200),@EndpointKey varchar(200),@Count int;
DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT Code,ModuleCode,FunctionCode,ActionCode,UiKey,EndpointKey FROM @Expected;
OPEN c; FETCH NEXT FROM c INTO @Code,@ModuleCode,@FunctionCode,@ActionCode,@UiKey,@EndpointKey;
WHILE @@FETCH_STATUS=0
BEGIN
    SET @Count=0;
    IF @Ready=1
    BEGIN
        SET @Sql=N'SELECT @Count=COUNT(*) FROM dbo.Permissions p JOIN dbo.Modules md ON md.Code=@ModuleCode AND md.IsActive=1 AND md.IsDeleted=0 JOIN dbo.Functions f ON f.ModuleId=md.Id AND f.Code=@FunctionCode AND f.IsActive=1 AND f.IsDeleted=0 JOIN dbo.FunctionActions fa ON fa.FunctionId=f.Id AND fa.Code=@ActionCode AND fa.UiKey=@UiKey AND fa.EndpointKey=@EndpointKey AND fa.IsActive=1 AND fa.IsDeleted=0 JOIN dbo.FunctionActionPermissions fap ON fap.FunctionActionId=fa.Id AND fap.PermissionId=p.Id WHERE p.Code=@Code AND p.ModuleCode=@ModuleCode AND p.FunctionCode=@FunctionCode AND p.ActionCode=@ActionCode AND p.IsActive=1 AND p.IsDeleted=0;';
        EXEC sys.sp_executesql @Sql,N'@Code varchar(200),@ModuleCode varchar(100),@FunctionCode varchar(100),@ActionCode varchar(100),@UiKey varchar(200),@EndpointKey varchar(200),@Count int OUTPUT',@Code,@ModuleCode,@FunctionCode,@ActionCode,@UiKey,@EndpointKey,@Count OUTPUT;
    END;
    IF @Count=1 BEGIN SET @Message=N'[PASS] '+@Code+N' has one active FunctionAction and FunctionActionPermissions mapping'; RAISERROR('%s',10,1,@Message) WITH NOWAIT; SET @Pass=@Pass+1; END
    ELSE BEGIN SET @Message=N'[FAIL] '+@Code+N' expected exactly one active action/mapping; found '+CONVERT(nvarchar(20),@Count); RAISERROR('%s',10,1,@Message) WITH NOWAIT; SET @Fail=@Fail+1; END;
    FETCH NEXT FROM c INTO @Code,@ModuleCode,@FunctionCode,@ActionCode,@UiKey,@EndpointKey;
END;
CLOSE c; DEALLOCATE c;

IF @Ready=1
BEGIN
    SET @Count=0;
    SELECT @Count=COUNT(*) FROM @Expected e
    JOIN dbo.Permissions p ON p.Code=e.Code
    JOIN dbo.FunctionActionPermissions fap ON fap.PermissionId=p.Id
    LEFT JOIN dbo.FunctionActions fa ON fa.Id=fap.FunctionActionId
    LEFT JOIN dbo.Functions f ON f.Id=fa.FunctionId
    LEFT JOIN dbo.Modules md ON md.Id=f.ModuleId
    WHERE fa.Id IS NULL OR md.Code<>e.ModuleCode OR f.Code<>e.FunctionCode OR fa.Code<>e.ActionCode
       OR md.Code IS NULL OR f.Code IS NULL;
    SELECT @Count=@Count+COUNT(*) FROM @Expected e
    JOIN dbo.FunctionActions fa ON fa.Code=e.ActionCode
    JOIN dbo.Functions f ON f.Id=fa.FunctionId AND f.Code=e.FunctionCode
    JOIN dbo.Modules md ON md.Id=f.ModuleId AND md.Code=e.ModuleCode
    JOIN dbo.FunctionActionPermissions fap ON fap.FunctionActionId=fa.Id
    LEFT JOIN dbo.Permissions p ON p.Id=fap.PermissionId
    WHERE p.Id IS NULL OR p.Code<>e.Code;
    IF @Count=0 BEGIN RAISERROR('[PASS] No unexpected CRM administration action/permission mappings',10,1) WITH NOWAIT; SET @Pass=@Pass+1; END
    ELSE BEGIN SET @Message=N'[FAIL] Unexpected CRM administration mappings = '+CONVERT(nvarchar(20),@Count); RAISERROR('%s',10,1,@Message) WITH NOWAIT; SET @Fail=@Fail+1; END;
    SET @Count=0; SELECT @Count=COUNT(*) FROM (SELECT UiKey FROM dbo.FunctionActions WHERE IsActive=1 AND IsDeleted=0 AND NULLIF(LTRIM(RTRIM(UiKey)),N'') IS NOT NULL GROUP BY UiKey HAVING COUNT(*)>1) d;
    IF @Count=0 BEGIN RAISERROR('[PASS] No duplicate active FunctionAction UiKey values',10,1) WITH NOWAIT; SET @Pass=@Pass+1; END ELSE BEGIN SET @Message=N'[FAIL] Duplicate active UiKey groups = '+CONVERT(nvarchar(20),@Count); RAISERROR('%s',10,1,@Message) WITH NOWAIT; SET @Fail=@Fail+1; END;
    SET @Count=0; SELECT @Count=COUNT(*) FROM (SELECT EndpointKey FROM dbo.FunctionActions WHERE IsActive=1 AND IsDeleted=0 AND NULLIF(LTRIM(RTRIM(EndpointKey)),N'') IS NOT NULL GROUP BY EndpointKey HAVING COUNT(*)>1) d;
    IF @Count=0 BEGIN RAISERROR('[PASS] No duplicate active FunctionAction EndpointKey values',10,1) WITH NOWAIT; SET @Pass=@Pass+1; END ELSE BEGIN SET @Message=N'[FAIL] Duplicate active EndpointKey groups = '+CONVERT(nvarchar(20),@Count); RAISERROR('%s',10,1,@Message) WITH NOWAIT; SET @Fail=@Fail+1; END;
END;

IF OBJECT_ID(N'dbo.RolePermissions',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.UserPermissions',N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.Roles',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.Users',N'U') IS NOT NULL
BEGIN
    SELECT @Count=COUNT(*) FROM dbo.RolePermissions rp JOIN dbo.Permissions p ON p.Id=rp.PermissionId JOIN @Expected e ON e.Code=p.Code;
    SET @Message=N'[PASS] Existing role grants for Phase1-008 permissions are reported (count='+CONVERT(nvarchar(20),@Count)+N'); seed does not create role grants'; RAISERROR('%s',10,1,@Message) WITH NOWAIT; SET @Pass=@Pass+1;
    SELECT @Count=COUNT(*) FROM dbo.UserPermissions up JOIN dbo.Permissions p ON p.Id=up.PermissionId JOIN @Expected e ON e.Code=p.Code;
    SET @Message=N'[PASS] Existing user grants/denies for Phase1-008 permissions are reported (count='+CONVERT(nvarchar(20),@Count)+N'); seed does not create user grants'; RAISERROR('%s',10,1,@Message) WITH NOWAIT; SET @Pass=@Pass+1;
    SELECT @Count=COUNT(*) FROM dbo.RolePermissions rp JOIN dbo.Roles r ON r.Id=rp.RoleId JOIN dbo.Permissions p ON p.Id=rp.PermissionId JOIN @Expected e ON e.Code=p.Code WHERE r.IsDeleted<>0 OR r.IsActive<>1 OR p.IsDeleted<>0 OR p.IsActive<>1;
    IF @Count=0 BEGIN RAISERROR('[PASS] No Phase1-008 role grant points to an inactive/deleted role or permission',10,1) WITH NOWAIT; SET @Pass=@Pass+1; END ELSE BEGIN SET @Message=N'[FAIL] Inactive/deleted Phase1-008 role grant references = '+CONVERT(nvarchar(20),@Count); RAISERROR('%s',10,1,@Message) WITH NOWAIT; SET @Fail=@Fail+1; END;
    SELECT @Count=COUNT(*) FROM dbo.UserPermissions up JOIN dbo.Users u ON u.Id=up.UserId JOIN dbo.Permissions p ON p.Id=up.PermissionId JOIN @Expected e ON e.Code=p.Code WHERE u.IsDeleted<>0 OR u.IsActive<>1 OR p.IsDeleted<>0 OR p.IsActive<>1;
    IF @Count=0 BEGIN RAISERROR('[PASS] No Phase1-008 user grant/deny points to an inactive/deleted user or permission',10,1) WITH NOWAIT; SET @Pass=@Pass+1; END ELSE BEGIN SET @Message=N'[FAIL] Inactive/deleted Phase1-008 user grant references = '+CONVERT(nvarchar(20),@Count); RAISERROR('%s',10,1,@Message) WITH NOWAIT; SET @Fail=@Fail+1; END;
END
ELSE
BEGIN SET @Message=N'[FAIL] Role/user authorization tables are missing; fail-closed assignment review cannot run'; RAISERROR('%s',10,1,@Message) WITH NOWAIT; SET @Fail=@Fail+1; END;

SET @Message=N'VerificationStatus='+CASE WHEN @Fail>0 THEN N'FAIL' ELSE N'PASS' END+N' | Passed='+CONVERT(nvarchar(20),@Pass)+N' | Failed='+CONVERT(nvarchar(20),@Fail);
RAISERROR('%s',10,1,@Message) WITH NOWAIT;
IF @Fail>0 RAISERROR('CRM Phase1-008 verification failed.',16,1);
GO