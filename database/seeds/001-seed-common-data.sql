/* TAPortal - Common seed data
   Idempotent, no passwords/secrets.
*/
USE [TAPortal];
GO

MERGE auth.DataScopes AS T
USING (VALUES
 ('SELF',N'Bản thân','SELF'),
 ('ASSIGNED',N'Được phân công','ASSIGNED'),
 ('TEAM',N'Theo team','TEAM'),
 ('BRANCH',N'Theo chi nhánh','BRANCH'),
 ('COMPANY',N'Theo công ty','COMPANY'),
 ('CUSTOM',N'Tùy chỉnh','CUSTOM')
) AS S(Code,Name,ScopeType)
ON T.Code = S.Code
WHEN NOT MATCHED THEN INSERT(Code,Name,ScopeType,IsSystem,IsActive) VALUES(S.Code,S.Name,S.ScopeType,1,1);
GO

MERGE sys.Modules AS T
USING (VALUES
 ('AUTH',N'Người dùng & phân quyền',10),
 ('ORG',N'Cơ cấu tổ chức',20),
 ('SYSTEM',N'Hệ thống',90),
 ('AUDIT',N'Nhật ký hệ thống',100)
) AS S(Code,Name,SortOrder)
ON T.Code = S.Code AND T.IsDeleted = 0
WHEN NOT MATCHED THEN INSERT(Code,Name,SortOrder,IsActive) VALUES(S.Code,S.Name,S.SortOrder,1);
GO

DECLARE @AuthModule uniqueidentifier=(SELECT TOP 1 Id FROM sys.Modules WHERE Code='AUTH' AND IsDeleted=0);
DECLARE @OrgModule uniqueidentifier=(SELECT TOP 1 Id FROM sys.Modules WHERE Code='ORG' AND IsDeleted=0);
DECLARE @SystemModule uniqueidentifier=(SELECT TOP 1 Id FROM sys.Modules WHERE Code='SYSTEM' AND IsDeleted=0);
DECLARE @AuditModule uniqueidentifier=(SELECT TOP 1 Id FROM sys.Modules WHERE Code='AUDIT' AND IsDeleted=0);

MERGE sys.Functions AS T
USING (VALUES
 (@AuthModule,'USERS',N'Người dùng','/system/users',10),
 (@AuthModule,'ROLES',N'Vai trò','/system/roles',20),
 (@AuthModule,'PERMISSIONS',N'Quyền','/system/permissions',30),
 (@OrgModule,'COMPANIES',N'Công ty','/organization/companies',10),
 (@OrgModule,'BRANCHES',N'Chi nhánh','/organization/branches',20),
 (@OrgModule,'TEAMS',N'Team','/organization/teams',30),
 (@SystemModule,'MENUS',N'Menu','/system/menus',10),
 (@SystemModule,'SETTINGS',N'Cấu hình','/system/settings',20),
 (@AuditModule,'AUDIT_LOGS',N'Nhật ký thao tác','/system/audit-logs',10)
) AS S(ModuleId,Code,Name,Route,SortOrder)
ON T.ModuleId=S.ModuleId AND T.Code=S.Code AND T.IsDeleted=0
WHEN NOT MATCHED THEN INSERT(ModuleId,Code,Name,Route,SortOrder,IsActive) VALUES(S.ModuleId,S.Code,S.Name,S.Route,S.SortOrder,1);
GO

;WITH PermissionSeed AS (
 SELECT * FROM (VALUES
 ('AUTH.USERS.VIEW',N'Xem người dùng','AUTH','USERS','VIEW'),
 ('AUTH.USERS.CREATE',N'Tạo người dùng','AUTH','USERS','CREATE'),
 ('AUTH.USERS.UPDATE',N'Cập nhật người dùng','AUTH','USERS','UPDATE'),
 ('AUTH.USERS.DELETE',N'Xóa người dùng','AUTH','USERS','DELETE'),
 ('AUTH.ROLES.VIEW',N'Xem vai trò','AUTH','ROLES','VIEW'),
 ('AUTH.ROLES.MANAGE',N'Quản lý vai trò','AUTH','ROLES','MANAGE'),
 ('AUTH.PERMISSIONS.VIEW',N'Xem quyền','AUTH','PERMISSIONS','VIEW'),
 ('AUTH.PERMISSIONS.MANAGE',N'Quản lý quyền','AUTH','PERMISSIONS','MANAGE'),
 ('ORG.COMPANIES.VIEW',N'Xem công ty','ORG','COMPANIES','VIEW'),
 ('ORG.COMPANIES.MANAGE',N'Quản lý công ty','ORG','COMPANIES','MANAGE'),
 ('ORG.BRANCHES.VIEW',N'Xem chi nhánh','ORG','BRANCHES','VIEW'),
 ('ORG.BRANCHES.MANAGE',N'Quản lý chi nhánh','ORG','BRANCHES','MANAGE'),
 ('ORG.TEAMS.VIEW',N'Xem team','ORG','TEAMS','VIEW'),
 ('ORG.TEAMS.MANAGE',N'Quản lý team','ORG','TEAMS','MANAGE'),
 ('SYSTEM.MENUS.MANAGE',N'Quản lý menu','SYSTEM','MENUS','MANAGE'),
 ('SYSTEM.SETTINGS.MANAGE',N'Quản lý cấu hình','SYSTEM','SETTINGS','MANAGE'),
 ('AUDIT.AUDIT_LOGS.VIEW',N'Xem nhật ký','AUDIT','AUDIT_LOGS','VIEW')
 ) V(Code,Name,ModuleCode,FunctionCode,ActionCode)
)
MERGE auth.Permissions AS T
USING PermissionSeed AS S
ON T.Code=S.Code AND T.IsDeleted=0
WHEN NOT MATCHED THEN
 INSERT(Code,Name,ModuleCode,FunctionCode,ActionCode,IsSystem,IsActive)
 VALUES(S.Code,S.Name,S.ModuleCode,S.FunctionCode,S.ActionCode,1,1);
GO

IF NOT EXISTS (SELECT 1 FROM auth.Roles WHERE Code='SYS_ADMIN' AND IsDeleted=0)
 INSERT auth.Roles(Code,Name,Description,IsSystem,IsActive)
 VALUES('SYS_ADMIN',N'Quản trị hệ thống',N'Vai trò quản trị toàn bộ common functions',1,1);
GO

DECLARE @AdminRole uniqueidentifier=(SELECT TOP 1 Id FROM auth.Roles WHERE Code='SYS_ADMIN' AND IsDeleted=0);
INSERT auth.RolePermissions(RoleId,PermissionId)
SELECT @AdminRole,p.Id
FROM auth.Permissions p
WHERE p.IsDeleted=0
  AND NOT EXISTS (SELECT 1 FROM auth.RolePermissions rp WHERE rp.RoleId=@AdminRole AND rp.PermissionId=p.Id);
GO
