/* FEATURE_ID: TAPORTAL-HR-MASTER-DATA
   CHANGE_ID: TAPORTAL-20261010-HR-MASTER-DATA-002
   PURPOSE: Additive Team master, hierarchy relationships, authorization and navigation reconciliation. */
SET NOCOUNT ON; SET XACT_ABORT ON;
GO
IF DB_NAME()<>N'TAPortalSys' THROW 52300,'This migration must run against TAPortalSys.',1;
IF OBJECT_ID(N'dbo.Departments',N'U') IS NULL THROW 52301,'Compatible dbo.Departments is required.',1;
IF COL_LENGTH(N'dbo.Departments',N'Id') IS NULL OR COL_LENGTH(N'dbo.Departments',N'IsActive') IS NULL OR COL_LENGTH(N'dbo.Departments',N'IsDeleted') IS NULL THROW 52302,'dbo.Departments is incompatible.',1;
IF OBJECT_ID(N'dbo.Modules',N'U') IS NULL OR OBJECT_ID(N'dbo.Functions',N'U') IS NULL OR OBJECT_ID(N'dbo.Permissions',N'U') IS NULL OR OBJECT_ID(N'dbo.FunctionActions',N'U') IS NULL OR OBJECT_ID(N'dbo.FunctionActionPermissions',N'U') IS NULL OR OBJECT_ID(N'dbo.Menus',N'U') IS NULL OR OBJECT_ID(N'dbo.MenuPermissions',N'U') IS NULL OR OBJECT_ID(N'dbo.Roles',N'U') IS NULL OR OBJECT_ID(N'dbo.RolePermissions',N'U') IS NULL THROW 52303,'Required authorization/navigation tables are missing.',1;
GO
BEGIN TRY
 BEGIN TRANSACTION;
 IF OBJECT_ID(N'dbo.Teams',N'U') IS NULL
  CREATE TABLE dbo.Teams(
   Id uniqueidentifier NOT NULL CONSTRAINT PK_Teams PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
   DepartmentId uniqueidentifier NOT NULL,ParentTeamId uniqueidentifier NULL,
   Code varchar(50) NOT NULL,Name nvarchar(250) NOT NULL,Description nvarchar(1000) NULL,
   SortOrder int NOT NULL CONSTRAINT DF_Teams_SortOrder DEFAULT 0,
   IsActive bit NOT NULL CONSTRAINT DF_Teams_IsActive DEFAULT 1,IsDeleted bit NOT NULL CONSTRAINT DF_Teams_IsDeleted DEFAULT 0,
   CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_Teams_CreatedAt DEFAULT SYSUTCDATETIME(),CreatedBy uniqueidentifier NULL,
   UpdatedAt datetime2(3) NULL,UpdatedBy uniqueidentifier NULL,DeletedAt datetime2(3) NULL,DeletedBy uniqueidentifier NULL,RowVersion rowversion NOT NULL);
 ELSE
 BEGIN
  IF COL_LENGTH(N'dbo.Teams',N'Id') IS NULL OR COL_LENGTH(N'dbo.Teams',N'Code') IS NULL OR COL_LENGTH(N'dbo.Teams',N'Name') IS NULL THROW 52304,'Existing dbo.Teams lacks required identity/code/name columns.',1;
  IF COL_LENGTH(N'dbo.Teams',N'DepartmentId') IS NULL BEGIN IF EXISTS(SELECT 1 FROM dbo.Teams) THROW 52305,'Existing Teams rows cannot be assigned safely to Departments.',1; ALTER TABLE dbo.Teams ADD DepartmentId uniqueidentifier NOT NULL; END;
  IF COL_LENGTH(N'dbo.Teams',N'ParentTeamId') IS NULL ALTER TABLE dbo.Teams ADD ParentTeamId uniqueidentifier NULL;
  IF COL_LENGTH(N'dbo.Teams',N'Description') IS NULL ALTER TABLE dbo.Teams ADD Description nvarchar(1000) NULL;
  IF COL_LENGTH(N'dbo.Teams',N'SortOrder') IS NULL ALTER TABLE dbo.Teams ADD SortOrder int NOT NULL CONSTRAINT DF_Teams_SortOrder DEFAULT 0 WITH VALUES;
  IF COL_LENGTH(N'dbo.Teams',N'IsActive') IS NULL ALTER TABLE dbo.Teams ADD IsActive bit NOT NULL CONSTRAINT DF_Teams_IsActive DEFAULT 1 WITH VALUES;
  IF COL_LENGTH(N'dbo.Teams',N'IsDeleted') IS NULL ALTER TABLE dbo.Teams ADD IsDeleted bit NOT NULL CONSTRAINT DF_Teams_IsDeleted DEFAULT 0 WITH VALUES;
  IF COL_LENGTH(N'dbo.Teams',N'CreatedAt') IS NULL ALTER TABLE dbo.Teams ADD CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_Teams_CreatedAt DEFAULT SYSUTCDATETIME() WITH VALUES;
  IF COL_LENGTH(N'dbo.Teams',N'CreatedBy') IS NULL ALTER TABLE dbo.Teams ADD CreatedBy uniqueidentifier NULL;
  IF COL_LENGTH(N'dbo.Teams',N'UpdatedAt') IS NULL ALTER TABLE dbo.Teams ADD UpdatedAt datetime2(3) NULL;
  IF COL_LENGTH(N'dbo.Teams',N'UpdatedBy') IS NULL ALTER TABLE dbo.Teams ADD UpdatedBy uniqueidentifier NULL;
  IF COL_LENGTH(N'dbo.Teams',N'DeletedAt') IS NULL ALTER TABLE dbo.Teams ADD DeletedAt datetime2(3) NULL;
  IF COL_LENGTH(N'dbo.Teams',N'DeletedBy') IS NULL ALTER TABLE dbo.Teams ADD DeletedBy uniqueidentifier NULL;
  IF COL_LENGTH(N'dbo.Teams',N'RowVersion') IS NULL ALTER TABLE dbo.Teams ADD RowVersion rowversion NOT NULL;
 END;
 IF EXISTS(SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id WHERE c.object_id=OBJECT_ID(N'dbo.Teams') AND ((c.name IN(N'Id',N'DepartmentId') AND (t.name<>N'uniqueidentifier' OR c.is_nullable=1)) OR (c.name=N'ParentTeamId' AND t.name<>N'uniqueidentifier') OR (c.name=N'Code' AND t.name<>N'varchar') OR (c.name IN(N'Name',N'Description') AND t.name<>N'nvarchar') OR (c.name=N'SortOrder' AND (t.name<>N'int' OR c.is_nullable=1)) OR (c.name IN(N'IsActive',N'IsDeleted') AND (t.name<>N'bit' OR c.is_nullable=1)))) THROW 52306,'dbo.Teams has incompatible required column types.',1;
 IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Teams') AND is_nullable=0 AND is_identity=0 AND default_object_id=0 AND name NOT IN(N'Id',N'DepartmentId',N'Code',N'Name',N'RowVersion')) THROW 52307,'Existing dbo.Teams has an unsupported required column without a default.',1;
 IF EXISTS(SELECT 1 FROM dbo.Teams t LEFT JOIN dbo.Departments d ON d.Id=t.DepartmentId WHERE d.Id IS NULL OR d.IsDeleted=1) THROW 52308,'Teams contains orphan/deleted Department references.',1;
 IF EXISTS(SELECT 1 FROM dbo.Teams t LEFT JOIN dbo.Teams p ON p.Id=t.ParentTeamId WHERE t.ParentTeamId IS NOT NULL AND (p.Id IS NULL OR p.IsDeleted=1)) THROW 52309,'Teams contains orphan/deleted ParentTeam references.',1;
 IF EXISTS(SELECT 1 FROM dbo.Teams t JOIN dbo.Teams p ON p.Id=t.ParentTeamId WHERE t.DepartmentId<>p.DepartmentId) THROW 52310,'Teams contains cross-department hierarchy.',1;
 IF EXISTS(SELECT 1 FROM dbo.Teams WHERE Id=ParentTeamId) THROW 52311,'Teams contains self-parent hierarchy.',1;
 DECLARE @Cycle bit=0;
 ;WITH HierarchyWalk AS(
  SELECT Id,ParentTeamId,CAST('/'+CONVERT(varchar(36),Id)+'/' AS varchar(max)) Path,CAST(0 AS bit) Cycle FROM dbo.Teams WHERE IsDeleted=0
  UNION ALL SELECT w.Id,p.ParentTeamId,CAST(w.Path+CONVERT(varchar(36),p.Id)+'/' AS varchar(max)),CAST(CASE WHEN CHARINDEX('/'+CONVERT(varchar(36),p.Id)+'/',w.Path)>0 THEN 1 ELSE 0 END AS bit)
  FROM HierarchyWalk w JOIN dbo.Teams p ON p.Id=w.ParentTeamId AND p.IsDeleted=0 WHERE w.Cycle=0)
 SELECT @Cycle=1 FROM HierarchyWalk WHERE Cycle=1 OPTION(MAXRECURSION 32767);
 IF @Cycle=1 THROW 52312,'Teams contains a hierarchy cycle.',1;
 IF EXISTS(SELECT Code FROM dbo.Teams WHERE IsDeleted=0 GROUP BY Code HAVING COUNT(*)>1) THROW 52313,'Duplicate nondeleted Team.Code values prevent unique indexing.',1;
 IF EXISTS(SELECT 1 FROM dbo.Teams WHERE LEN(LTRIM(RTRIM(Code)))=0 OR Code<>UPPER(LTRIM(RTRIM(Code))) OR LEN(LTRIM(RTRIM(Name)))=0) THROW 52314,'Existing Team code/name violates canonical rules.',1;
 IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.Teams') AND name=N'CK_Teams_Code_Canonical') ALTER TABLE dbo.Teams WITH CHECK ADD CONSTRAINT CK_Teams_Code_Canonical CHECK(Code=UPPER(LTRIM(RTRIM(Code))) AND LEN(Code)>0);
 IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.Teams') AND name=N'CK_Teams_Name_NotBlank') ALTER TABLE dbo.Teams WITH CHECK ADD CONSTRAINT CK_Teams_Name_NotBlank CHECK(LEN(LTRIM(RTRIM(Name)))>0);
 IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.Teams') AND name=N'CK_Teams_NotSelfParent') ALTER TABLE dbo.Teams WITH CHECK ADD CONSTRAINT CK_Teams_NotSelfParent CHECK(ParentTeamId IS NULL OR ParentTeamId<>Id);
 IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.Teams') AND name=N'FK_Teams_Departments') ALTER TABLE dbo.Teams WITH CHECK ADD CONSTRAINT FK_Teams_Departments FOREIGN KEY(DepartmentId) REFERENCES dbo.Departments(Id);
 IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.Teams') AND name=N'FK_Teams_ParentTeam') ALTER TABLE dbo.Teams WITH CHECK ADD CONSTRAINT FK_Teams_ParentTeam FOREIGN KEY(ParentTeamId) REFERENCES dbo.Teams(Id);
 IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Teams') AND name=N'UX_Teams_Code_NotDeleted') CREATE UNIQUE INDEX UX_Teams_Code_NotDeleted ON dbo.Teams(Code) WHERE IsDeleted=0;
 IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Teams') AND name=N'IX_Teams_DepartmentId') CREATE INDEX IX_Teams_DepartmentId ON dbo.Teams(DepartmentId,IsDeleted,SortOrder);
 IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Teams') AND name=N'IX_Teams_ParentTeamId') CREATE INDEX IX_Teams_ParentTeamId ON dbo.Teams(ParentTeamId) WHERE ParentTeamId IS NOT NULL;

 DECLARE @ModuleId uniqueidentifier,@FunctionId uniqueidentifier,@HrMenuId uniqueidentifier,@MenuId uniqueidentifier,@AdminRoleId uniqueidentifier;
 SELECT @ModuleId=Id FROM dbo.Modules WHERE Code='HR' AND IsDeleted=0; IF @ModuleId IS NULL THROW 52315,'Existing HR module is required.',1;
 SELECT @FunctionId=Id FROM dbo.Functions WHERE ModuleId=@ModuleId AND Code='TEAMS' AND IsDeleted=0;
 IF @FunctionId IS NULL BEGIN SET @FunctionId=NEWID(); INSERT dbo.Functions(Id,ModuleId,Code,Name,Description,Route,SortOrder,IsActive,IsDeleted) VALUES(@FunctionId,@ModuleId,'TEAMS',N'Team',N'Quản lý Team',N'/hr/teams',20,1,0); END ELSE UPDATE dbo.Functions SET Name=N'Team',Route=N'/hr/teams',SortOrder=20,IsActive=1 WHERE Id=@FunctionId;
 DECLARE @Permissions TABLE(Code varchar(200),Name nvarchar(250),ActionCode varchar(50)); INSERT @Permissions VALUES('HR.TEAM.VIEW',N'Xem Team','VIEW'),('HR.TEAM.CREATE',N'Tạo Team','CREATE'),('HR.TEAM.UPDATE',N'Cập nhật Team','UPDATE'),('HR.TEAM.DELETE',N'Xóa Team','DELETE');
 INSERT dbo.Permissions(Id,Code,Name,ModuleCode,FunctionCode,ActionCode,Description,IsSystem,IsActive,IsDeleted) SELECT NEWID(),x.Code,x.Name,'HR','TEAMS',x.ActionCode,N'Quyền quản lý Team',1,1,0 FROM @Permissions x WHERE NOT EXISTS(SELECT 1 FROM dbo.Permissions p WHERE p.Code=x.Code AND p.IsDeleted=0);
 UPDATE p SET p.Name=x.Name,p.ModuleCode='HR',p.FunctionCode='TEAMS',p.ActionCode=x.ActionCode,p.IsActive=1 FROM dbo.Permissions p JOIN @Permissions x ON x.Code=p.Code WHERE p.IsDeleted=0;
 DECLARE @Actions TABLE(Code varchar(100),Name nvarchar(200),UiKey varchar(200),EndpointKey varchar(200),SortOrder int,PermissionCode varchar(200)); INSERT @Actions VALUES('VIEW',N'Xem Team','teams.view','Teams.Index',10,'HR.TEAM.VIEW'),('CREATE',N'Tạo Team','teams.create','Teams.Create',20,'HR.TEAM.CREATE'),('UPDATE',N'Cập nhật Team','teams.update','Teams.Update',30,'HR.TEAM.UPDATE'),('DELETE',N'Xóa Team','teams.delete','Teams.Delete',40,'HR.TEAM.DELETE');
 IF EXISTS(SELECT 1 FROM @Actions x JOIN dbo.FunctionActions fa ON (fa.UiKey=x.UiKey OR fa.EndpointKey=x.EndpointKey) AND fa.FunctionId<>@FunctionId AND fa.IsDeleted=0) THROW 52316,'Team action key conflicts with another function.',1;
 INSERT dbo.FunctionActions(Id,FunctionId,Code,Name,UiKey,EndpointKey,SortOrder,IsActive,IsDeleted) SELECT NEWID(),@FunctionId,x.Code,x.Name,x.UiKey,x.EndpointKey,x.SortOrder,1,0 FROM @Actions x WHERE NOT EXISTS(SELECT 1 FROM dbo.FunctionActions fa WHERE fa.FunctionId=@FunctionId AND fa.Code=x.Code AND fa.IsDeleted=0);
 UPDATE fa SET fa.Name=x.Name,fa.UiKey=x.UiKey,fa.EndpointKey=x.EndpointKey,fa.SortOrder=x.SortOrder,fa.IsActive=1 FROM dbo.FunctionActions fa JOIN @Actions x ON x.Code=fa.Code WHERE fa.FunctionId=@FunctionId AND fa.IsDeleted=0;
 INSERT dbo.FunctionActionPermissions(FunctionActionId,PermissionId) SELECT fa.Id,p.Id FROM @Actions x JOIN dbo.FunctionActions fa ON fa.FunctionId=@FunctionId AND fa.Code=x.Code AND fa.IsDeleted=0 JOIN dbo.Permissions p ON p.Code=x.PermissionCode AND p.IsDeleted=0 WHERE NOT EXISTS(SELECT 1 FROM dbo.FunctionActionPermissions z WHERE z.FunctionActionId=fa.Id AND z.PermissionId=p.Id);
 SELECT @HrMenuId=Id FROM dbo.Menus WHERE Code='HR' AND IsDeleted=0; IF @HrMenuId IS NULL THROW 52317,'Existing HR menu is required.',1;
 SELECT @MenuId=Id FROM dbo.Menus WHERE Code='HR_TEAMS' AND IsDeleted=0; IF @MenuId IS NULL BEGIN SET @MenuId=NEWID(); INSERT dbo.Menus(Id,ParentId,ModuleId,FunctionId,Code,Name,Icon,Route,SortOrder,IsVisible,IsActive,IsDeleted) VALUES(@MenuId,@HrMenuId,@ModuleId,@FunctionId,'HR_TEAMS',N'Team',N'ti ti-hierarchy-2',N'/hr/teams',20,1,1,0); END ELSE UPDATE dbo.Menus SET ParentId=@HrMenuId,ModuleId=@ModuleId,FunctionId=@FunctionId,Name=N'Team',Icon=N'ti ti-hierarchy-2',Route=N'/hr/teams',SortOrder=20,IsVisible=1,IsActive=1 WHERE Id=@MenuId;
 INSERT dbo.MenuPermissions(MenuId,PermissionId) SELECT @MenuId,p.Id FROM dbo.Permissions p WHERE p.Code='HR.TEAM.VIEW' AND p.IsDeleted=0 AND NOT EXISTS(SELECT 1 FROM dbo.MenuPermissions mp WHERE mp.MenuId=@MenuId AND mp.PermissionId=p.Id);
 SELECT @AdminRoleId=Id FROM dbo.Roles WHERE Code='SYS_ADMIN' AND IsDeleted=0 AND IsActive=1; IF @AdminRoleId IS NULL THROW 52318,'Active SYS_ADMIN role is required.',1;
 INSERT dbo.RolePermissions(RoleId,PermissionId) SELECT @AdminRoleId,p.Id FROM dbo.Permissions p JOIN @Permissions x ON x.Code=p.Code WHERE p.IsDeleted=0 AND NOT EXISTS(SELECT 1 FROM dbo.RolePermissions rp WHERE rp.RoleId=@AdminRoleId AND rp.PermissionId=p.Id);
 IF (SELECT COUNT(*) FROM dbo.Permissions p JOIN @Permissions x ON x.Code=p.Code WHERE p.IsDeleted=0 AND p.IsActive=1)<>4 THROW 52319,'Team permission postflight failed.',1;
 IF (SELECT COUNT(*) FROM dbo.FunctionActions fa JOIN @Actions x ON x.Code=fa.Code WHERE fa.FunctionId=@FunctionId AND fa.IsDeleted=0 AND fa.IsActive=1)<>4 THROW 52320,'Team FunctionAction postflight failed.',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.MenuPermissions mp JOIN dbo.Permissions p ON p.Id=mp.PermissionId WHERE mp.MenuId=@MenuId AND p.Code='HR.TEAM.VIEW' AND p.IsDeleted=0) THROW 52321,'Team menu permission postflight failed.',1;
 COMMIT; PRINT '[PASS] 056 HR Team master migration completed.';
END TRY BEGIN CATCH IF XACT_STATE()<>0 ROLLBACK; THROW; END CATCH;
GO
