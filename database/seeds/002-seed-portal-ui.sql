/* TAPortal - Portal UI seed 002
   Idempotent. No plaintext passwords are stored here.
   The initial admin account is created by TAPortal.Web at startup and hashed before insert.
*/
USE [TAPortal];
GO

MERGE dbo.Modules AS T
USING (VALUES ('CRM',N'Khách hàng',30)) AS S(Code,Name,SortOrder)
ON T.Code=S.Code AND T.IsDeleted=0
WHEN MATCHED THEN UPDATE SET Name=S.Name,SortOrder=S.SortOrder,IsActive=1
WHEN NOT MATCHED THEN INSERT(Code,Name,SortOrder,IsActive) VALUES(S.Code,S.Name,S.SortOrder,1);
GO

DECLARE @CrmModule uniqueidentifier=(SELECT TOP 1 Id FROM dbo.Modules WHERE Code='CRM' AND IsDeleted=0);
MERGE dbo.Functions AS T
USING (VALUES (@CrmModule,'CUSTOMERS',N'Khách hàng','/customers',10)) AS S(ModuleId,Code,Name,Route,SortOrder)
ON T.ModuleId=S.ModuleId AND T.Code=S.Code AND T.IsDeleted=0
WHEN MATCHED THEN UPDATE SET Name=S.Name,Route=S.Route,SortOrder=S.SortOrder,IsActive=1
WHEN NOT MATCHED THEN INSERT(ModuleId,Code,Name,Route,SortOrder,IsActive) VALUES(S.ModuleId,S.Code,S.Name,S.Route,S.SortOrder,1);
GO

MERGE dbo.Permissions AS T
USING (VALUES
 ('CRM.CUSTOMERS.VIEW',N'Xem khách hàng','CRM','CUSTOMERS','VIEW'),
 ('CRM.CUSTOMERS.CREATE',N'Tạo khách hàng','CRM','CUSTOMERS','CREATE'),
 ('CRM.CUSTOMERS.UPDATE',N'Cập nhật khách hàng','CRM','CUSTOMERS','UPDATE'),
 ('CRM.CUSTOMERS.DELETE',N'Xóa khách hàng','CRM','CUSTOMERS','DELETE')
) AS S(Code,Name,ModuleCode,FunctionCode,ActionCode)
ON T.Code=S.Code AND T.IsDeleted=0
WHEN MATCHED THEN UPDATE SET Name=S.Name,ModuleCode=S.ModuleCode,FunctionCode=S.FunctionCode,ActionCode=S.ActionCode,IsActive=1
WHEN NOT MATCHED THEN INSERT(Code,Name,ModuleCode,FunctionCode,ActionCode,IsSystem,IsActive) VALUES(S.Code,S.Name,S.ModuleCode,S.FunctionCode,S.ActionCode,1,1);
GO

DECLARE @AuthModule uniqueidentifier=(SELECT TOP 1 Id FROM dbo.Modules WHERE Code='AUTH' AND IsDeleted=0);
DECLARE @SystemModule uniqueidentifier=(SELECT TOP 1 Id FROM dbo.Modules WHERE Code='SYSTEM' AND IsDeleted=0);
DECLARE @AuditModule uniqueidentifier=(SELECT TOP 1 Id FROM dbo.Modules WHERE Code='AUDIT' AND IsDeleted=0);
DECLARE @UserFn uniqueidentifier=(SELECT TOP 1 f.Id FROM dbo.Functions f JOIN dbo.Modules m ON m.Id=f.ModuleId WHERE m.Code='AUTH' AND f.Code='USERS' AND f.IsDeleted=0);
DECLARE @RoleFn uniqueidentifier=(SELECT TOP 1 f.Id FROM dbo.Functions f JOIN dbo.Modules m ON m.Id=f.ModuleId WHERE m.Code='AUTH' AND f.Code='ROLES' AND f.IsDeleted=0);
DECLARE @CustomerFn uniqueidentifier=(SELECT TOP 1 f.Id FROM dbo.Functions f JOIN dbo.Modules m ON m.Id=f.ModuleId WHERE m.Code='CRM' AND f.Code='CUSTOMERS' AND f.IsDeleted=0);
DECLARE @MenusFn uniqueidentifier=(SELECT TOP 1 f.Id FROM dbo.Functions f JOIN dbo.Modules m ON m.Id=f.ModuleId WHERE m.Code='SYSTEM' AND f.Code='MENUS' AND f.IsDeleted=0);
DECLARE @SettingsFn uniqueidentifier=(SELECT TOP 1 f.Id FROM dbo.Functions f JOIN dbo.Modules m ON m.Id=f.ModuleId WHERE m.Code='SYSTEM' AND f.Code='SETTINGS' AND f.IsDeleted=0);
DECLARE @AuditFn uniqueidentifier=(SELECT TOP 1 f.Id FROM dbo.Functions f JOIN dbo.Modules m ON m.Id=f.ModuleId WHERE m.Code='AUDIT' AND f.Code='AUDIT_LOGS' AND f.IsDeleted=0);

MERGE dbo.Menus AS T
USING (VALUES
 (NULL,NULL,NULL,'DASHBOARD',N'Tổng quan','ti ti-layout-dashboard','/',10),
 (NULL,@CrmModule,@CustomerFn,'CUSTOMERS',N'Khách hàng','ti ti-building-store','/customers',20),
 (NULL,@AuthModule,@UserFn,'USERS',N'Người dùng','ti ti-users','/users',30),
 (NULL,@AuthModule,@RoleFn,'ROLES',N'Vai trò & Phân quyền','ti ti-shield-lock','/roles',40),
 (NULL,@SystemModule,@MenusFn,'MENUS',N'Quản lý Menu','ti ti-list-details','/menus',50),
 (NULL,@SystemModule,@SettingsFn,'SETTINGS',N'Cấu hình hệ thống','ti ti-settings','/system/settings',60),
 (NULL,@AuditModule,@AuditFn,'AUDIT_LOGS',N'Nhật ký hoạt động','ti ti-history','/system/audit-logs',70)
) AS S(ParentId,ModuleId,FunctionId,Code,Name,Icon,Route,SortOrder)
ON T.Code=S.Code AND T.IsDeleted=0
WHEN MATCHED THEN UPDATE SET Name=S.Name,Icon=S.Icon,Route=S.Route,SortOrder=S.SortOrder,ModuleId=S.ModuleId,FunctionId=S.FunctionId,IsVisible=1,IsActive=1
WHEN NOT MATCHED THEN INSERT(ParentId,ModuleId,FunctionId,Code,Name,Icon,Route,SortOrder,IsVisible,IsActive) VALUES(S.ParentId,S.ModuleId,S.FunctionId,S.Code,S.Name,S.Icon,S.Route,S.SortOrder,1,1);
GO

DECLARE @AdminRole uniqueidentifier=(SELECT TOP 1 Id FROM dbo.Roles WHERE Code='SYS_ADMIN' AND IsDeleted=0);
IF @AdminRole IS NOT NULL
INSERT dbo.RolePermissions(RoleId,PermissionId)
SELECT @AdminRole,p.Id FROM dbo.Permissions p
WHERE p.IsDeleted=0 AND NOT EXISTS(SELECT 1 FROM dbo.RolePermissions rp WHERE rp.RoleId=@AdminRole AND rp.PermissionId=p.Id);
GO

PRINT '002-seed-portal-ui.sql: OK';
GO
