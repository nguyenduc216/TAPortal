/*
 FEATURE_ID: TAPORTAL-CRM-CUSTOMER-360
 CHANGE_ID: TAPORTAL-20261014-CRM-PHASE1-008-DB
 PURPOSE: Register the CRM customer-administration permission metadata used by
          the Customers.Assign / Customers.Share / Customers.Team actions.
 SAFETY:
 - Manual execution only. Codex never runs this file.
 - No DDL, no DROP/TRUNCATE/DELETE, no Users/UserRoles/RolePermissions changes.
 - Idempotent, key-based; never hard-codes identifiers.
 - Grants no role or user privilege; an administrator must assign it explicitly.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_NAME() <> N'TAPortalSys' THROW 52408, 'Wrong database; expected TAPortalSys.', 1;

IF OBJECT_ID(N'dbo.Permissions', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Modules', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Functions', N'U') IS NULL
   OR OBJECT_ID(N'dbo.FunctionActions', N'U') IS NULL
   OR OBJECT_ID(N'dbo.FunctionActionPermissions', N'U') IS NULL
    THROW 52409, 'Required authorization metadata table is missing.', 1;
IF COL_LENGTH(N'dbo.Permissions', N'Code') IS NULL
   OR COL_LENGTH(N'dbo.Permissions', N'ModuleCode') IS NULL
   OR COL_LENGTH(N'dbo.Permissions', N'FunctionCode') IS NULL
   OR COL_LENGTH(N'dbo.Permissions', N'ActionCode') IS NULL
   OR COL_LENGTH(N'dbo.Permissions', N'IsActive') IS NULL
   OR COL_LENGTH(N'dbo.Permissions', N'IsDeleted') IS NULL
    THROW 52410, 'dbo.Permissions is incompatible with the CRM permission contract.', 1;
IF COL_LENGTH(N'dbo.Modules', N'Id') IS NULL OR COL_LENGTH(N'dbo.Modules', N'Code') IS NULL
   OR COL_LENGTH(N'dbo.Functions', N'Id') IS NULL OR COL_LENGTH(N'dbo.Functions', N'ModuleId') IS NULL
   OR COL_LENGTH(N'dbo.Functions', N'Code') IS NULL
   OR COL_LENGTH(N'dbo.FunctionActions', N'Id') IS NULL OR COL_LENGTH(N'dbo.FunctionActions', N'FunctionId') IS NULL
   OR COL_LENGTH(N'dbo.FunctionActions', N'Code') IS NULL OR COL_LENGTH(N'dbo.FunctionActions', N'Name') IS NULL
   OR COL_LENGTH(N'dbo.FunctionActions', N'UiKey') IS NULL OR COL_LENGTH(N'dbo.FunctionActions', N'EndpointKey') IS NULL
   OR COL_LENGTH(N'dbo.FunctionActions', N'IsActive') IS NULL OR COL_LENGTH(N'dbo.FunctionActions', N'IsDeleted') IS NULL
   OR COL_LENGTH(N'dbo.FunctionActionPermissions', N'FunctionActionId') IS NULL
   OR COL_LENGTH(N'dbo.FunctionActionPermissions', N'PermissionId') IS NULL
    THROW 52413, 'Authorization metadata tables are incompatible with the CRM action contract.', 1;

BEGIN TRY
 BEGIN TRANSACTION;

  DECLARE @seed TABLE(Code varchar(150), Name nvarchar(200), ModuleCode varchar(50), FunctionCode varchar(100), ActionCode varchar(50), UiKey varchar(200), EndpointKey varchar(200), SortOrder int);
 INSERT @seed VALUES
   ('CRM.CUSTOMERS.ASSIGN',N'Phân công khách hàng','CRM','Customers','Assign','customers.assign','Customers.Assign',30),
   ('CRM.CUSTOMERS.SHARE',N'Chia sẻ khách hàng','CRM','Customers','Share','customers.share','Customers.Share',40),
   ('CRM.CUSTOMERS.TEAM',N'Gán team dịch vụ khách hàng','CRM','Customers','Team','customers.team','Customers.Team',50);

 IF EXISTS (
   SELECT 1 FROM @seed s
   JOIN dbo.Permissions p ON p.Code=s.Code AND p.IsDeleted=1
 )
   THROW 52411, 'A soft-deleted CRM permission with the same code requires manual review.', 1;

 IF EXISTS (SELECT 1 FROM @seed s JOIN dbo.Permissions p ON p.Code=s.Code
            WHERE p.IsActive<>1 OR p.IsDeleted<>0 OR p.ModuleCode<>s.ModuleCode OR p.FunctionCode<>s.FunctionCode
               OR p.ActionCode<>s.ActionCode OR p.ModuleCode IS NULL OR p.FunctionCode IS NULL)
   THROW 52419, 'Existing CRM permission has inactive/deleted or conflicting module/function/action metadata.', 1;

 IF EXISTS (SELECT 1 FROM @seed s LEFT JOIN dbo.Modules md ON md.Code=s.ModuleCode AND md.IsDeleted=0 AND md.IsActive=1
            LEFT JOIN dbo.Functions f ON f.ModuleId=md.Id AND f.Code=s.FunctionCode AND f.IsDeleted=0 AND f.IsActive=1
            WHERE md.Id IS NULL OR f.Id IS NULL)
   THROW 52414, 'Active CRM Customers module/function metadata is missing.', 1;

 INSERT dbo.Permissions(Code,Name,ModuleCode,FunctionCode,ActionCode,IsSystem,IsActive)
 SELECT s.Code,s.Name,s.ModuleCode,s.FunctionCode,s.ActionCode,1,1 FROM @seed s
 WHERE NOT EXISTS (SELECT 1 FROM dbo.Permissions p WHERE p.Code=s.Code);

  IF (SELECT COUNT(*) FROM dbo.Permissions p JOIN @seed s ON s.Code=p.Code
       WHERE p.IsDeleted=0 AND p.IsActive=1 AND p.ModuleCode='CRM' AND p.FunctionCode='Customers') <> 3
   THROW 52412, 'CRM customer administration permission postflight failed.', 1;

  IF EXISTS (
      SELECT 1 FROM @seed s
      LEFT JOIN dbo.Modules md ON md.Code=s.ModuleCode AND md.IsDeleted=0 AND md.IsActive=1
      LEFT JOIN dbo.Functions f ON f.ModuleId=md.Id AND f.Code=s.FunctionCode AND f.IsDeleted=0 AND f.IsActive=1
      WHERE md.Id IS NULL OR f.Id IS NULL
  ) THROW 52414, 'Active CRM Customers module/function metadata is missing.', 1;

  IF EXISTS (
      SELECT 1 FROM @seed s
      JOIN dbo.Modules md ON md.Code=s.ModuleCode AND md.IsDeleted=0
      JOIN dbo.Functions f ON f.ModuleId=md.Id AND f.Code=s.FunctionCode AND f.IsDeleted=0
      JOIN dbo.FunctionActions fa ON fa.FunctionId=f.Id AND fa.Code=s.ActionCode AND fa.IsDeleted=0
      WHERE fa.IsActive<>1 OR fa.UiKey IS NULL OR fa.UiKey<>s.UiKey
         OR fa.EndpointKey IS NULL OR fa.EndpointKey<>s.EndpointKey
  ) THROW 52415, 'Existing CRM FunctionAction has a conflicting key.', 1;

  INSERT dbo.FunctionActions(FunctionId,Code,Name,UiKey,EndpointKey,SortOrder,IsActive,IsDeleted)
  SELECT f.Id,s.ActionCode,s.Name,s.UiKey,s.EndpointKey,s.SortOrder,1,0
  FROM @seed s
  JOIN dbo.Modules md ON md.Code=s.ModuleCode AND md.IsDeleted=0 AND md.IsActive=1
  JOIN dbo.Functions f ON f.ModuleId=md.Id AND f.Code=s.FunctionCode AND f.IsDeleted=0 AND f.IsActive=1
  WHERE NOT EXISTS (SELECT 1 FROM dbo.FunctionActions fa WHERE fa.FunctionId=f.Id AND fa.Code=s.ActionCode AND fa.IsDeleted=0);

  IF EXISTS (SELECT 1 FROM dbo.FunctionActions fa JOIN @seed s ON fa.UiKey=s.UiKey OR fa.EndpointKey=s.EndpointKey
             LEFT JOIN dbo.Functions f ON f.Id=fa.FunctionId
             LEFT JOIN dbo.Modules md ON md.Id=f.ModuleId
             WHERE fa.IsActive=1 AND fa.IsDeleted=0
               AND (md.Code<>s.ModuleCode OR f.Code<>s.FunctionCode OR fa.Code<>s.ActionCode
                    OR md.Code IS NULL OR f.Code IS NULL))
    THROW 52420, 'CRM action key is already owned by another FunctionAction.', 1;

  IF EXISTS (SELECT 1 FROM @seed s JOIN dbo.Modules md ON md.Code=s.ModuleCode AND md.IsDeleted=0 JOIN dbo.Functions f ON f.ModuleId=md.Id AND f.Code=s.FunctionCode AND f.IsDeleted=0 JOIN dbo.FunctionActions fa ON fa.FunctionId=f.Id AND fa.Code=s.ActionCode AND fa.IsDeleted=0 WHERE fa.IsActive=0)
    THROW 52416, 'An expected CRM FunctionAction is inactive.', 1;

  IF EXISTS (SELECT 1 FROM @seed s
    JOIN dbo.Modules md ON md.Code=s.ModuleCode AND md.IsDeleted=0
    JOIN dbo.Functions f ON f.ModuleId=md.Id AND f.Code=s.FunctionCode AND f.IsDeleted=0
    JOIN dbo.FunctionActions fa ON fa.FunctionId=f.Id AND fa.Code=s.ActionCode AND fa.IsDeleted=0
    JOIN dbo.Permissions p ON p.Code=s.Code AND p.IsDeleted=0
    JOIN dbo.FunctionActionPermissions fap ON fap.FunctionActionId=fa.Id OR fap.PermissionId=p.Id
    WHERE (fap.FunctionActionId=fa.Id AND fap.PermissionId<>p.Id)
       OR (fap.PermissionId=p.Id AND fap.FunctionActionId<>fa.Id))
    THROW 52421, 'CRM action/permission has an unexpected mapping; manual review required.', 1;

  INSERT dbo.FunctionActionPermissions(FunctionActionId,PermissionId)
  SELECT fa.Id,p.Id
  FROM @seed s
  JOIN dbo.Modules md ON md.Code=s.ModuleCode AND md.IsDeleted=0 AND md.IsActive=1
  JOIN dbo.Functions f ON f.ModuleId=md.Id AND f.Code=s.FunctionCode AND f.IsDeleted=0 AND f.IsActive=1
  JOIN dbo.FunctionActions fa ON fa.FunctionId=f.Id AND fa.Code=s.ActionCode AND fa.IsDeleted=0 AND fa.IsActive=1
  JOIN dbo.Permissions p ON p.Code=s.Code AND p.IsDeleted=0 AND p.IsActive=1
  WHERE NOT EXISTS (SELECT 1 FROM dbo.FunctionActionPermissions fap WHERE fap.FunctionActionId=fa.Id AND fap.PermissionId=p.Id);

  IF EXISTS (
      SELECT 1 FROM @seed s
      JOIN dbo.Modules md ON md.Code=s.ModuleCode AND md.IsDeleted=0 AND md.IsActive=1
      JOIN dbo.Functions f ON f.ModuleId=md.Id AND f.Code=s.FunctionCode AND f.IsDeleted=0 AND f.IsActive=1
      JOIN dbo.FunctionActions fa ON fa.FunctionId=f.Id AND fa.Code=s.ActionCode AND fa.IsDeleted=0 AND fa.IsActive=1
      JOIN dbo.Permissions p ON p.Code=s.Code AND p.IsDeleted=0 AND p.IsActive=1
      LEFT JOIN dbo.FunctionActionPermissions fap ON fap.FunctionActionId=fa.Id AND fap.PermissionId=p.Id
      WHERE fap.FunctionActionId IS NULL
  ) THROW 52417, 'CRM FunctionActionPermissions postflight failed.', 1;

  IF EXISTS (SELECT UiKey FROM dbo.FunctionActions WHERE IsDeleted=0 AND IsActive=1 AND NULLIF(LTRIM(RTRIM(UiKey)),N'') IS NOT NULL GROUP BY UiKey HAVING COUNT(*)>1)
     OR EXISTS (SELECT EndpointKey FROM dbo.FunctionActions WHERE IsDeleted=0 AND IsActive=1 AND NULLIF(LTRIM(RTRIM(EndpointKey)),N'') IS NOT NULL GROUP BY EndpointKey HAVING COUNT(*)>1)
    THROW 52418, 'Active FunctionAction key uniqueness postflight failed.', 1;

 COMMIT TRANSACTION;
  PRINT '[PASS] 008 CRM customer administration permissions and action mappings registered.';
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
GO

SELECT Code, FunctionCode, ActionCode, IsActive, IsDeleted
FROM dbo.Permissions
WHERE Code IN ('CRM.CUSTOMERS.ASSIGN','CRM.CUSTOMERS.SHARE','CRM.CUSTOMERS.TEAM')
ORDER BY Code;