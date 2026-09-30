/* TAPortal - Navigation hierarchy + compact portal menu
   008. Idempotent. Safe to re-run after 001-007 + seeds.
*/
USE [TAPortal];
GO

DECLARE @CrmModule uniqueidentifier=(SELECT TOP 1 Id FROM dbo.Modules WHERE Code='CRM' AND IsDeleted=0);
DECLARE @AuthModule uniqueidentifier=(SELECT TOP 1 Id FROM dbo.Modules WHERE Code='AUTH' AND IsDeleted=0);
DECLARE @SystemModule uniqueidentifier=(SELECT TOP 1 Id FROM dbo.Modules WHERE Code='SYSTEM' AND IsDeleted=0);
DECLARE @AuditModule uniqueidentifier=(SELECT TOP 1 Id FROM dbo.Modules WHERE Code='AUDIT' AND IsDeleted=0);

MERGE dbo.Menus AS T
USING (VALUES
 ('CRM_GROUP',N'Khách hàng & đối tác','ti ti-building-community',20,@CrmModule),
 ('AUTH_GROUP',N'Người dùng & phân quyền','ti ti-shield-lock',70,@AuthModule),
 ('SYSTEM_GROUP',N'Quản trị hệ thống','ti ti-settings',90,@SystemModule)
) AS S(Code,Name,Icon,SortOrder,ModuleId)
ON T.Code=S.Code AND T.IsDeleted=0
WHEN MATCHED THEN UPDATE SET Name=S.Name,Icon=S.Icon,Route=NULL,SortOrder=S.SortOrder,ModuleId=S.ModuleId,FunctionId=NULL,ParentId=NULL,IsVisible=1,IsActive=1,UpdatedAt=SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT(ParentId,ModuleId,FunctionId,Code,Name,Icon,Route,SortOrder,IsVisible,IsActive)
VALUES(NULL,S.ModuleId,NULL,S.Code,S.Name,S.Icon,NULL,S.SortOrder,1,1);
GO

DECLARE @CrmGroup uniqueidentifier=(SELECT TOP 1 Id FROM dbo.Menus WHERE Code='CRM_GROUP' AND IsDeleted=0);
DECLARE @AuthGroup uniqueidentifier=(SELECT TOP 1 Id FROM dbo.Menus WHERE Code='AUTH_GROUP' AND IsDeleted=0);
DECLARE @SystemGroup uniqueidentifier=(SELECT TOP 1 Id FROM dbo.Menus WHERE Code='SYSTEM_GROUP' AND IsDeleted=0);

UPDATE dbo.Menus SET ParentId=NULL,SortOrder=10,Name=N'Tổng quan',Icon='ti ti-layout-dashboard',Route='/',IsVisible=1,IsActive=1,UpdatedAt=SYSUTCDATETIME()
WHERE Code='DASHBOARD' AND IsDeleted=0;

UPDATE dbo.Menus SET ParentId=@CrmGroup,SortOrder=10,Name=N'Khách hàng',Icon='ti ti-users-group',Route='/customers',IsVisible=1,IsActive=1,UpdatedAt=SYSUTCDATETIME()
WHERE Code='CUSTOMERS' AND IsDeleted=0;

UPDATE dbo.Menus SET ParentId=@AuthGroup,SortOrder=10,Name=N'Người dùng',Icon='ti ti-users',Route='/users',IsVisible=1,IsActive=1,UpdatedAt=SYSUTCDATETIME()
WHERE Code='USERS' AND IsDeleted=0;
UPDATE dbo.Menus SET ParentId=@AuthGroup,SortOrder=20,Name=N'Vai trò & phân quyền',Icon='ti ti-user-shield',Route='/roles',IsVisible=1,IsActive=1,UpdatedAt=SYSUTCDATETIME()
WHERE Code='ROLES' AND IsDeleted=0;

UPDATE dbo.Menus SET ParentId=@SystemGroup,SortOrder=10,Name=N'Quản lý menu',Icon='ti ti-list-tree',Route='/menus',IsVisible=1,IsActive=1,UpdatedAt=SYSUTCDATETIME()
WHERE Code='MENUS' AND IsDeleted=0;
UPDATE dbo.Menus SET ParentId=@SystemGroup,SortOrder=20,Name=N'Cấu hình hệ thống',Icon='ti ti-adjustments',Route='/system/settings',IsVisible=1,IsActive=1,UpdatedAt=SYSUTCDATETIME()
WHERE Code='SETTINGS' AND IsDeleted=0;
UPDATE dbo.Menus SET ParentId=@SystemGroup,SortOrder=30,Name=N'Nhật ký hoạt động',Icon='ti ti-history',Route='/system/audit-logs',IsVisible=1,IsActive=1,UpdatedAt=SYSUTCDATETIME()
WHERE Code='AUDIT_LOGS' AND IsDeleted=0;
GO

/* Roadmap groups are seeded hidden so Menu Administration can activate them
   only after the corresponding controllers/features are deployed. */
MERGE dbo.Menus AS T
USING (VALUES
 ('BANKING_GROUP',N'Ngân hàng & thanh toán','ti ti-building-bank',30),
 ('INVOICE_GROUP',N'Hóa đơn','ti ti-receipt-2',40),
 ('BILLING_GROUP',N'Gói dịch vụ','ti ti-package',50),
 ('INTEGRATION_GROUP',N'Tích hợp','ti ti-plug-connected',60)
) AS S(Code,Name,Icon,SortOrder)
ON T.Code=S.Code AND T.IsDeleted=0
WHEN MATCHED THEN UPDATE SET Name=S.Name,Icon=S.Icon,SortOrder=S.SortOrder,Route=NULL,ParentId=NULL,IsVisible=0,IsActive=1,UpdatedAt=SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT(Code,Name,Icon,SortOrder,IsVisible,IsActive) VALUES(S.Code,S.Name,S.Icon,S.SortOrder,0,1);
GO

CREATE INDEX IX_Menus_Parent_Visible_Sort
ON dbo.Menus(ParentId,IsVisible,IsActive,SortOrder)
INCLUDE(Code,Name,Icon,Route)
WHERE IsDeleted=0;
GO

PRINT '008-navigation-hierarchy.sql: OK';
GO
