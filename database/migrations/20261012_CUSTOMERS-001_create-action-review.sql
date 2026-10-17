/*
   FEATURE_ID: TAPORTAL-SECURITY-ADMIN-AUTHORIZATION
   CHANGE_ID: TAPORTAL-20261012-CUSTOMERS-ACTION-METADATA-004
   PURPOSE: Add only the missing Customers.Create action metadata.

   EXECUTION POLICY:
   - REVIEW/APPROVAL REQUIRED. Do not run against production without an approved change.
   - No Users, UserRoles, UserPermissions, Roles, or RolePermissions rows are changed.
   - Additive, idempotent, transactional, and fail-closed on schema/conflicts.
   - Rollback plan: restore the pre-change backup or, after owner review, remove only
     rows identified by post-change verification and having no later dependencies.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> N'TAPortalSys' THROW 52201, 'Wrong database; expected TAPortalSys.', 1;
IF OBJECT_ID(N'dbo.Functions',N'U') IS NULL OR OBJECT_ID(N'dbo.FunctionActions',N'U') IS NULL OR OBJECT_ID(N'dbo.Permissions',N'U') IS NULL OR OBJECT_ID(N'dbo.FunctionActionPermissions',N'U') IS NULL
    THROW 52202, 'Required authorization metadata table is missing.', 1;
IF COL_LENGTH(N'dbo.Functions',N'Id') IS NULL OR COL_LENGTH(N'dbo.Functions',N'Code') IS NULL OR COL_LENGTH(N'dbo.Functions',N'IsDeleted') IS NULL OR COL_LENGTH(N'dbo.FunctionActions',N'Id') IS NULL OR COL_LENGTH(N'dbo.FunctionActions',N'FunctionId') IS NULL OR COL_LENGTH(N'dbo.FunctionActions',N'Code') IS NULL OR COL_LENGTH(N'dbo.FunctionActions',N'Name') IS NULL OR COL_LENGTH(N'dbo.FunctionActions',N'UiKey') IS NULL OR COL_LENGTH(N'dbo.FunctionActions',N'EndpointKey') IS NULL OR COL_LENGTH(N'dbo.FunctionActions',N'SortOrder') IS NULL OR COL_LENGTH(N'dbo.FunctionActions',N'IsActive') IS NULL OR COL_LENGTH(N'dbo.FunctionActions',N'IsDeleted') IS NULL OR COL_LENGTH(N'dbo.FunctionActions',N'CreatedAt') IS NULL OR COL_LENGTH(N'dbo.Permissions',N'Id') IS NULL OR COL_LENGTH(N'dbo.Permissions',N'Code') IS NULL OR COL_LENGTH(N'dbo.Permissions',N'Name') IS NULL OR COL_LENGTH(N'dbo.Permissions',N'ModuleCode') IS NULL OR COL_LENGTH(N'dbo.Permissions',N'FunctionCode') IS NULL OR COL_LENGTH(N'dbo.Permissions',N'ActionCode') IS NULL OR COL_LENGTH(N'dbo.Permissions',N'IsActive') IS NULL OR COL_LENGTH(N'dbo.Permissions',N'IsDeleted') IS NULL OR COL_LENGTH(N'dbo.Permissions',N'CreatedAt') IS NULL OR COL_LENGTH(N'dbo.FunctionActionPermissions',N'FunctionActionId') IS NULL OR COL_LENGTH(N'dbo.FunctionActionPermissions',N'PermissionId') IS NULL
    THROW 52203, 'Authorization metadata schema does not match the expected contract.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.FunctionActions') AND name=N'UX_FunctionActions_EndpointKey' AND is_unique=1) THROW 52204, 'Expected unique active EndpointKey index is missing.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.FunctionActions') AND name=N'UX_FunctionActions_UiKey' AND is_unique=1) THROW 52205, 'Expected unique active UiKey index is missing.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Permissions') AND name=N'UX_Permissions_Code' AND is_unique=1) THROW 52206, 'Expected unique active permission Code index is missing.', 1;

SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @functionId uniqueidentifier,@permissionId uniqueidentifier,@actionId uniqueidentifier,@n int;
    SELECT @n=COUNT(*) FROM dbo.Functions WITH (UPDLOCK,HOLDLOCK) WHERE Code='CUSTOMERS' AND IsDeleted=0;
    IF @n<>1 THROW 52207, 'Expected exactly one active CUSTOMERS function.', 1;
    SELECT @functionId=Id FROM dbo.Functions WHERE Code='CUSTOMERS' AND IsDeleted=0;
    IF EXISTS (SELECT 1 FROM dbo.Functions WHERE Code='CUSTOMERS' AND IsDeleted=1) THROW 52208, 'Deleted CUSTOMERS function requires manual review.', 1;

    SELECT @n=COUNT(*) FROM dbo.Permissions WITH (UPDLOCK,HOLDLOCK) WHERE Code='CRM.CUSTOMERS.CREATE' AND IsDeleted=0;
    IF @n>1 THROW 52209, 'Duplicate active CRM.CUSTOMERS.CREATE permissions exist.', 1;
    IF EXISTS (SELECT 1 FROM dbo.Permissions WHERE Code='CRM.CUSTOMERS.CREATE' AND IsDeleted=1) THROW 52210, 'Deleted CRM.CUSTOMERS.CREATE permission requires manual review.', 1;
    IF @n=1
    BEGIN
        SELECT @permissionId=Id FROM dbo.Permissions WHERE Code='CRM.CUSTOMERS.CREATE' AND IsDeleted=0;
        IF EXISTS (SELECT 1 FROM dbo.Permissions WHERE Id=@permissionId AND (IsActive=0 OR ModuleCode<>'CRM' OR FunctionCode<>'CUSTOMERS' OR ActionCode<>'CREATE')) THROW 52211, 'Existing create permission has conflicting metadata.', 1;
    END
    ELSE
    BEGIN
        SET @permissionId=NEWID();
        INSERT dbo.Permissions(Id,Code,Name,ModuleCode,FunctionCode,ActionCode,IsSystem,IsActive,IsDeleted,CreatedAt) VALUES(@permissionId,'CRM.CUSTOMERS.CREATE',N'Thêm khách hàng','CRM','CUSTOMERS','CREATE',0,1,0,SYSUTCDATETIME());
    END;

    SELECT @n=COUNT(*) FROM dbo.FunctionActions WITH (UPDLOCK,HOLDLOCK) WHERE FunctionId=@functionId AND Code='CREATE' AND IsDeleted=0;
    IF @n>1 THROW 52212, 'Duplicate active Customers CREATE actions exist.', 1;
    IF EXISTS (SELECT 1 FROM dbo.FunctionActions WHERE FunctionId=@functionId AND Code='CREATE' AND IsDeleted=1) THROW 52213, 'Deleted Customers CREATE action requires manual review.', 1;
    IF @n=1
    BEGIN
        SELECT @actionId=Id FROM dbo.FunctionActions WHERE FunctionId=@functionId AND Code='CREATE' AND IsDeleted=0;
        IF EXISTS (SELECT 1 FROM dbo.FunctionActions WHERE Id=@actionId AND (IsActive=0 OR ISNULL(UiKey,'')<>'customers.create' OR ISNULL(EndpointKey,'')<>'Customers.Create')) THROW 52214, 'Existing create action has conflicting metadata.', 1;
    END
    ELSE
    BEGIN
        SET @actionId=NEWID();
        INSERT dbo.FunctionActions(Id,FunctionId,Code,Name,UiKey,EndpointKey,SortOrder,IsActive,IsDeleted,CreatedAt) VALUES(@actionId,@functionId,'CREATE',N'Thêm khách hàng','customers.create','Customers.Create',20,1,0,SYSUTCDATETIME());
    END;

    IF EXISTS (SELECT 1 FROM dbo.FunctionActionPermissions WITH (UPDLOCK,HOLDLOCK) WHERE FunctionActionId=@actionId AND PermissionId<>@permissionId) THROW 52215, 'Create action has an unexpected permission mapping.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.FunctionActionPermissions WITH (UPDLOCK,HOLDLOCK) WHERE FunctionActionId=@actionId AND PermissionId=@permissionId)
        INSERT dbo.FunctionActionPermissions(FunctionActionId,PermissionId,CreatedAt) VALUES(@actionId,@permissionId,SYSUTCDATETIME());
    IF (SELECT COUNT(*) FROM dbo.FunctionActions WHERE Id=@actionId AND FunctionId=@functionId AND Code='CREATE' AND UiKey='customers.create' AND EndpointKey='Customers.Create' AND IsActive=1 AND IsDeleted=0)<>1 THROW 52216, 'Postcondition failed for Customers CREATE action.', 1;
    IF (SELECT COUNT(*) FROM dbo.Permissions WHERE Id=@permissionId AND Code='CRM.CUSTOMERS.CREATE' AND ModuleCode='CRM' AND FunctionCode='CUSTOMERS' AND ActionCode='CREATE' AND IsActive=1 AND IsDeleted=0)<>1 THROW 52217, 'Postcondition failed for create permission.', 1;
    IF (SELECT COUNT(*) FROM dbo.FunctionActionPermissions WHERE FunctionActionId=@actionId AND PermissionId=@permissionId)<>1 THROW 52218, 'Postcondition failed for create mapping.', 1;
    COMMIT;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK;
    THROW;
END CATCH;

SELECT fa.Id AS ActionId,fa.FunctionId,fa.Code,fa.UiKey,fa.EndpointKey,fa.IsActive,fa.IsDeleted,p.Id AS PermissionId,p.Code AS PermissionCode,p.IsActive AS PermissionActive,p.IsDeleted AS PermissionDeleted
FROM dbo.FunctionActions fa JOIN dbo.FunctionActionPermissions fap ON fap.FunctionActionId=fa.Id JOIN dbo.Permissions p ON p.Id=fap.PermissionId
WHERE fa.EndpointKey='Customers.Create' AND fa.IsActive=1 AND fa.IsDeleted=0;