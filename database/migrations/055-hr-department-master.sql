/*
FEATURE_ID: TAPORTAL-HR-MASTER-DATA
CHANGE_ID: TAPORTAL-20261010-HR-MASTER-DATA-001
PURPOSE: Additive TAPortalSys Department master, dynamic actions, menu, and SYS_ADMIN grants.
Migration 054 is intentionally not reused: the known runtime navigation seed is absent from source and its exact executed content cannot be reconstructed safely.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO
IF DB_NAME()<>N'TAPortalSys' THROW 52200,'This migration must run against TAPortalSys.',1;
GO
IF OBJECT_ID(N'dbo.Modules',N'U') IS NULL OR OBJECT_ID(N'dbo.Functions',N'U') IS NULL OR OBJECT_ID(N'dbo.Permissions',N'U') IS NULL
   OR OBJECT_ID(N'dbo.FunctionActions',N'U') IS NULL OR OBJECT_ID(N'dbo.FunctionActionPermissions',N'U') IS NULL
   OR OBJECT_ID(N'dbo.Menus',N'U') IS NULL OR OBJECT_ID(N'dbo.MenuPermissions',N'U') IS NULL
   OR OBJECT_ID(N'dbo.Roles',N'U') IS NULL OR OBJECT_ID(N'dbo.RolePermissions',N'U') IS NULL
    THROW 52201,'Required TAPortalSys authorization/navigation tables are missing.',1;
GO
BEGIN TRY
 BEGIN TRANSACTION;

 IF OBJECT_ID(N'dbo.Departments',N'U') IS NULL
 BEGIN
  CREATE TABLE dbo.Departments(
   Id uniqueidentifier NOT NULL CONSTRAINT PK_Departments PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
   Code varchar(50) NOT NULL,
   Name nvarchar(250) NOT NULL,
   Description nvarchar(1000) NULL,
   SortOrder int NOT NULL CONSTRAINT DF_Departments_SortOrder DEFAULT 0,
   IsActive bit NOT NULL CONSTRAINT DF_Departments_IsActive DEFAULT 1,
   IsDeleted bit NOT NULL CONSTRAINT DF_Departments_IsDeleted DEFAULT 0,
   CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_Departments_CreatedAt DEFAULT SYSUTCDATETIME(),
   CreatedBy uniqueidentifier NULL,UpdatedAt datetime2(3) NULL,UpdatedBy uniqueidentifier NULL,
   DeletedAt datetime2(3) NULL,DeletedBy uniqueidentifier NULL,
   RowVersion rowversion NOT NULL
  );
 END
 ELSE
 BEGIN
  IF COL_LENGTH(N'dbo.Departments',N'Id') IS NULL OR COL_LENGTH(N'dbo.Departments',N'Code') IS NULL OR COL_LENGTH(N'dbo.Departments',N'Name') IS NULL
   THROW 52202,'Existing dbo.Departments lacks required identity/code/name columns.',1;
  IF COL_LENGTH(N'dbo.Departments',N'Description') IS NULL ALTER TABLE dbo.Departments ADD Description nvarchar(1000) NULL;
  IF COL_LENGTH(N'dbo.Departments',N'SortOrder') IS NULL ALTER TABLE dbo.Departments ADD SortOrder int NOT NULL CONSTRAINT DF_Departments_SortOrder DEFAULT 0 WITH VALUES;
  IF COL_LENGTH(N'dbo.Departments',N'IsActive') IS NULL ALTER TABLE dbo.Departments ADD IsActive bit NOT NULL CONSTRAINT DF_Departments_IsActive DEFAULT 1 WITH VALUES;
  IF COL_LENGTH(N'dbo.Departments',N'IsDeleted') IS NULL ALTER TABLE dbo.Departments ADD IsDeleted bit NOT NULL CONSTRAINT DF_Departments_IsDeleted DEFAULT 0 WITH VALUES;
  IF COL_LENGTH(N'dbo.Departments',N'CreatedAt') IS NULL ALTER TABLE dbo.Departments ADD CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_Departments_CreatedAt DEFAULT SYSUTCDATETIME() WITH VALUES;
  IF COL_LENGTH(N'dbo.Departments',N'CreatedBy') IS NULL ALTER TABLE dbo.Departments ADD CreatedBy uniqueidentifier NULL;
  IF COL_LENGTH(N'dbo.Departments',N'UpdatedAt') IS NULL ALTER TABLE dbo.Departments ADD UpdatedAt datetime2(3) NULL;
  IF COL_LENGTH(N'dbo.Departments',N'UpdatedBy') IS NULL ALTER TABLE dbo.Departments ADD UpdatedBy uniqueidentifier NULL;
  IF COL_LENGTH(N'dbo.Departments',N'DeletedAt') IS NULL ALTER TABLE dbo.Departments ADD DeletedAt datetime2(3) NULL;
  IF COL_LENGTH(N'dbo.Departments',N'DeletedBy') IS NULL ALTER TABLE dbo.Departments ADD DeletedBy uniqueidentifier NULL;
  IF COL_LENGTH(N'dbo.Departments',N'RowVersion') IS NULL ALTER TABLE dbo.Departments ADD RowVersion rowversion NOT NULL;
 END;

 IF EXISTS(SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id WHERE c.object_id=OBJECT_ID(N'dbo.Departments') AND ((c.name=N'Id' AND (t.name<>N'uniqueidentifier' OR c.is_nullable=1)) OR (c.name=N'Code' AND t.name<>N'varchar') OR (c.name IN(N'Name',N'Description') AND t.name<>N'nvarchar') OR (c.name=N'SortOrder' AND (t.name<>N'int' OR c.is_nullable=1)) OR (c.name IN(N'IsActive',N'IsDeleted') AND (t.name<>N'bit' OR c.is_nullable=1))))
  THROW 52203,'dbo.Departments has incompatible required column types.',1;
 IF EXISTS(SELECT Code FROM dbo.Departments WHERE IsDeleted=0 GROUP BY Code HAVING COUNT(*)>1)
  THROW 52204,'Duplicate nondeleted Department.Code values prevent unique index creation.',1;
 IF EXISTS(SELECT 1 FROM dbo.Departments WHERE LEN(LTRIM(RTRIM(Code)))=0 OR Code<>UPPER(LTRIM(RTRIM(Code))) OR LEN(LTRIM(RTRIM(Name)))=0)
  THROW 52210,'Existing Department code/name values violate canonical master-data rules.',1;
 IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.Departments') AND name=N'CK_Departments_Code_Canonical')
  ALTER TABLE dbo.Departments WITH CHECK ADD CONSTRAINT CK_Departments_Code_Canonical CHECK(Code=UPPER(LTRIM(RTRIM(Code))) AND LEN(Code)>0);
 IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.Departments') AND name=N'CK_Departments_Name_NotBlank')
  ALTER TABLE dbo.Departments WITH CHECK ADD CONSTRAINT CK_Departments_Name_NotBlank CHECK(LEN(LTRIM(RTRIM(Name)))>0);
 IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Departments') AND name=N'UX_Departments_Code_NotDeleted')
  CREATE UNIQUE INDEX UX_Departments_Code_NotDeleted ON dbo.Departments(Code) WHERE IsDeleted=0;

 DECLARE @ModuleId uniqueidentifier,@FunctionId uniqueidentifier,@HrMenuId uniqueidentifier,@MenuId uniqueidentifier,@AdminRoleId uniqueidentifier;
 SELECT @ModuleId=Id FROM dbo.Modules WHERE Code='HR' AND IsDeleted=0;
 IF @ModuleId IS NULL BEGIN SET @ModuleId=NEWID(); INSERT dbo.Modules(Id,Code,Name,Description,SortOrder,IsActive,IsDeleted) VALUES(@ModuleId,'HR',N'Nhân sự',N'Quản lý dữ liệu nhân sự',30,1,0); END
 ELSE UPDATE dbo.Modules SET Name=N'Nhân sự',IsActive=1 WHERE Id=@ModuleId;

 SELECT @FunctionId=Id FROM dbo.Functions WHERE ModuleId=@ModuleId AND Code='DEPARTMENTS' AND IsDeleted=0;
 IF @FunctionId IS NULL BEGIN SET @FunctionId=NEWID(); INSERT dbo.Functions(Id,ModuleId,Code,Name,Description,Route,SortOrder,IsActive,IsDeleted) VALUES(@FunctionId,@ModuleId,'DEPARTMENTS',N'Phòng ban',N'Quản lý phòng ban',N'/hr/departments',10,1,0); END
 ELSE UPDATE dbo.Functions SET Name=N'Phòng ban',Route=N'/hr/departments',IsActive=1 WHERE Id=@FunctionId;

 DECLARE @Permissions TABLE(Code varchar(200),Name nvarchar(250),ActionCode varchar(50));
 INSERT @Permissions VALUES('HR.DEPARTMENT.VIEW',N'Xem phòng ban','VIEW'),('HR.DEPARTMENT.CREATE',N'Tạo phòng ban','CREATE'),('HR.DEPARTMENT.UPDATE',N'Cập nhật phòng ban','UPDATE'),('HR.DEPARTMENT.DELETE',N'Xóa phòng ban','DELETE');
 INSERT dbo.Permissions(Id,Code,Name,ModuleCode,FunctionCode,ActionCode,Description,IsSystem,IsActive,IsDeleted)
 SELECT NEWID(),x.Code,x.Name,'HR','DEPARTMENTS',x.ActionCode,N'Quyền quản lý master phòng ban',1,1,0 FROM @Permissions x WHERE NOT EXISTS(SELECT 1 FROM dbo.Permissions p WHERE p.Code=x.Code AND p.IsDeleted=0);
 UPDATE p SET p.Name=x.Name,p.ModuleCode='HR',p.FunctionCode='DEPARTMENTS',p.ActionCode=x.ActionCode,p.IsActive=1 FROM dbo.Permissions p JOIN @Permissions x ON x.Code=p.Code WHERE p.IsDeleted=0;

 DECLARE @Actions TABLE(Code varchar(100),Name nvarchar(200),UiKey varchar(200),EndpointKey varchar(200),SortOrder int,PermissionCode varchar(200));
 INSERT @Actions VALUES('VIEW',N'Xem phòng ban','departments.view','Departments.Index',10,'HR.DEPARTMENT.VIEW'),('CREATE',N'Tạo phòng ban','departments.create','Departments.Create',20,'HR.DEPARTMENT.CREATE'),('UPDATE',N'Cập nhật phòng ban','departments.update','Departments.Update',30,'HR.DEPARTMENT.UPDATE'),('DELETE',N'Xóa phòng ban','departments.delete','Departments.Delete',40,'HR.DEPARTMENT.DELETE');
 IF EXISTS(SELECT 1 FROM @Actions x JOIN dbo.FunctionActions fa ON (fa.UiKey=x.UiKey OR fa.EndpointKey=x.EndpointKey) AND fa.FunctionId<>@FunctionId AND fa.IsDeleted=0)
  THROW 52205,'Department action key conflicts with an existing FunctionAction.',1;
 INSERT dbo.FunctionActions(Id,FunctionId,Code,Name,UiKey,EndpointKey,SortOrder,IsActive,IsDeleted)
 SELECT NEWID(),@FunctionId,x.Code,x.Name,x.UiKey,x.EndpointKey,x.SortOrder,1,0 FROM @Actions x WHERE NOT EXISTS(SELECT 1 FROM dbo.FunctionActions fa WHERE fa.FunctionId=@FunctionId AND fa.Code=x.Code AND fa.IsDeleted=0);
 UPDATE fa SET fa.Name=x.Name,fa.UiKey=x.UiKey,fa.EndpointKey=x.EndpointKey,fa.SortOrder=x.SortOrder,fa.IsActive=1 FROM dbo.FunctionActions fa JOIN @Actions x ON x.Code=fa.Code WHERE fa.FunctionId=@FunctionId AND fa.IsDeleted=0;
 INSERT dbo.FunctionActionPermissions(FunctionActionId,PermissionId)
 SELECT fa.Id,p.Id FROM @Actions x JOIN dbo.FunctionActions fa ON fa.FunctionId=@FunctionId AND fa.Code=x.Code AND fa.IsDeleted=0 JOIN dbo.Permissions p ON p.Code=x.PermissionCode AND p.IsDeleted=0 WHERE NOT EXISTS(SELECT 1 FROM dbo.FunctionActionPermissions fap WHERE fap.FunctionActionId=fa.Id AND fap.PermissionId=p.Id);

 SELECT @HrMenuId=Id FROM dbo.Menus WHERE Code='HR' AND IsDeleted=0;
 IF @HrMenuId IS NULL BEGIN SET @HrMenuId=NEWID(); INSERT dbo.Menus(Id,ParentId,ModuleId,FunctionId,Code,Name,Icon,Route,SortOrder,IsVisible,IsActive,IsDeleted) VALUES(@HrMenuId,NULL,@ModuleId,NULL,'HR',N'Nhân sự',N'ti ti-users-group',NULL,30,1,1,0); END
 ELSE UPDATE dbo.Menus SET ModuleId=@ModuleId,Name=N'Nhân sự',Icon=N'ti ti-users-group',IsVisible=1,IsActive=1 WHERE Id=@HrMenuId;
 SELECT @MenuId=Id FROM dbo.Menus WHERE Code='HR_DEPARTMENTS' AND IsDeleted=0;
 IF @MenuId IS NULL BEGIN SET @MenuId=NEWID(); INSERT dbo.Menus(Id,ParentId,ModuleId,FunctionId,Code,Name,Icon,Route,SortOrder,IsVisible,IsActive,IsDeleted) VALUES(@MenuId,@HrMenuId,@ModuleId,@FunctionId,'HR_DEPARTMENTS',N'Phòng ban',N'ti ti-building',N'/hr/departments',10,1,1,0); END
 ELSE UPDATE dbo.Menus SET ParentId=@HrMenuId,ModuleId=@ModuleId,FunctionId=@FunctionId,Name=N'Phòng ban',Icon=N'ti ti-building',Route=N'/hr/departments',IsVisible=1,IsActive=1 WHERE Id=@MenuId;
 INSERT dbo.MenuPermissions(MenuId,PermissionId) SELECT @MenuId,p.Id FROM dbo.Permissions p WHERE p.Code='HR.DEPARTMENT.VIEW' AND p.IsDeleted=0 AND NOT EXISTS(SELECT 1 FROM dbo.MenuPermissions mp WHERE mp.MenuId=@MenuId AND mp.PermissionId=p.Id);

 SELECT @AdminRoleId=Id FROM dbo.Roles WHERE Code='SYS_ADMIN' AND IsDeleted=0 AND IsActive=1;
 IF @AdminRoleId IS NULL THROW 52206,'Active SYS_ADMIN role is required.',1;
 INSERT dbo.RolePermissions(RoleId,PermissionId) SELECT @AdminRoleId,p.Id FROM dbo.Permissions p JOIN @Permissions x ON x.Code=p.Code WHERE p.IsDeleted=0 AND NOT EXISTS(SELECT 1 FROM dbo.RolePermissions rp WHERE rp.RoleId=@AdminRoleId AND rp.PermissionId=p.Id);

 IF (SELECT COUNT(*) FROM dbo.Permissions p JOIN @Permissions x ON x.Code=p.Code WHERE p.IsDeleted=0 AND p.IsActive=1)<>4 THROW 52207,'Department permission postflight failed.',1;
 IF (SELECT COUNT(*) FROM dbo.FunctionActions WHERE FunctionId=@FunctionId AND IsDeleted=0 AND IsActive=1) < 4 THROW 52208,'Department FunctionAction postflight failed.',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.MenuPermissions mp JOIN dbo.Permissions p ON p.Id=mp.PermissionId WHERE mp.MenuId=@MenuId AND p.Code='HR.DEPARTMENT.VIEW' AND p.IsDeleted=0) THROW 52209,'Department menu permission postflight failed.',1;
 COMMIT;
 PRINT '[PASS] 055 HR Department master migration completed.';
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK;
 THROW;
END CATCH;
GO
