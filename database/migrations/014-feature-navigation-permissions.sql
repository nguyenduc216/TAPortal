/* TAPortal 014 - Feature modules, permissions and navigation. SQL Server 2014 compatible. */
USE [TAPortal];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO
DECLARE @M TABLE(Code varchar(50),Name nvarchar(200),SortOrder int);
INSERT @M VALUES('PARTNER',N'Đối tác',20),('BANKING',N'Ngân hàng & thanh toán',40),('INVOICE',N'Hóa đơn',50),('BILLING',N'Gói dịch vụ',60),('INTEGRATION',N'Tích hợp',70);
UPDATE t SET Name=s.Name,SortOrder=s.SortOrder,IsActive=1 FROM dbo.Modules t JOIN @M s ON s.Code=t.Code WHERE t.IsDeleted=0;
INSERT dbo.Modules(Code,Name,SortOrder,IsActive) SELECT s.Code,s.Name,s.SortOrder,1 FROM @M s WHERE NOT EXISTS(SELECT 1 FROM dbo.Modules t WHERE t.Code=s.Code AND t.IsDeleted=0);
GO
DECLARE @F TABLE(ModuleCode varchar(50),Code varchar(50),Name nvarchar(200),Route nvarchar(500),SortOrder int);
INSERT @F VALUES('PARTNER','PARTNERS',N'Đối tác','/partners',10),('BANKING','BANK_ACCOUNTS',N'Tài khoản ngân hàng','/banking/accounts',10),('BANKING','TRANSACTIONS',N'Giao dịch ngân hàng','/banking/transactions',20),('BANKING','PAYMENTS',N'Yêu cầu thanh toán','/payments',30),('INVOICE','INVOICES',N'Hóa đơn','/invoices',10),('BILLING','CREDITS',N'Số dư Credit','/billing/credits',10),('INTEGRATION','PROVIDERS',N'Nhà cung cấp','/integrations/providers',10);
UPDATE t SET Name=s.Name,Route=s.Route,SortOrder=s.SortOrder,IsActive=1 FROM dbo.Functions t JOIN dbo.Modules m ON m.Id=t.ModuleId JOIN @F s ON s.ModuleCode=m.Code AND s.Code=t.Code WHERE t.IsDeleted=0 AND m.IsDeleted=0;
INSERT dbo.Functions(ModuleId,Code,Name,Route,SortOrder,IsActive) SELECT m.Id,s.Code,s.Name,s.Route,s.SortOrder,1 FROM @F s JOIN dbo.Modules m ON m.Code=s.ModuleCode AND m.IsDeleted=0 WHERE NOT EXISTS(SELECT 1 FROM dbo.Functions t WHERE t.ModuleId=m.Id AND t.Code=s.Code AND t.IsDeleted=0);
GO
DECLARE @P TABLE(Code varchar(150),Name nvarchar(200),ModuleCode varchar(50),FunctionCode varchar(50),ActionCode varchar(50));
INSERT @P VALUES('PARTNER.PARTNERS.VIEW',N'Xem đối tác','PARTNER','PARTNERS','VIEW'),('BANKING.BANK_ACCOUNTS.VIEW',N'Xem tài khoản ngân hàng','BANKING','BANK_ACCOUNTS','VIEW'),('BANKING.TRANSACTIONS.VIEW',N'Xem giao dịch ngân hàng','BANKING','TRANSACTIONS','VIEW'),('BANKING.PAYMENTS.VIEW',N'Xem yêu cầu thanh toán','BANKING','PAYMENTS','VIEW'),('INVOICE.INVOICES.VIEW',N'Xem hóa đơn','INVOICE','INVOICES','VIEW'),('BILLING.CREDITS.VIEW',N'Xem số dư credit','BILLING','CREDITS','VIEW'),('INTEGRATION.PROVIDERS.VIEW',N'Xem nhà cung cấp','INTEGRATION','PROVIDERS','VIEW');
UPDATE t SET Name=s.Name,IsActive=1 FROM dbo.Permissions t JOIN @P s ON s.Code=t.Code WHERE t.IsDeleted=0;
INSERT dbo.Permissions(Code,Name,ModuleCode,FunctionCode,ActionCode,IsSystem,IsActive) SELECT s.Code,s.Name,s.ModuleCode,s.FunctionCode,s.ActionCode,1,1 FROM @P s WHERE NOT EXISTS(SELECT 1 FROM dbo.Permissions t WHERE t.Code=s.Code AND t.IsDeleted=0);
GO
DECLARE @N TABLE(ParentCode varchar(50),FunctionCode varchar(50),Code varchar(50),Name nvarchar(200),Icon varchar(100),Route nvarchar(500),SortOrder int);
INSERT @N VALUES('CRM_GROUP','PARTNERS','PARTNERS',N'Đối tác','ti ti-building-community','/partners',5),('BANKING_GROUP','BANK_ACCOUNTS','BANK_ACCOUNTS',N'Tài khoản ngân hàng','ti ti-building-bank','/banking/accounts',10),('BANKING_GROUP','TRANSACTIONS','BANK_TRANSACTIONS',N'Giao dịch ngân hàng','ti ti-arrows-exchange','/banking/transactions',20),('BANKING_GROUP','PAYMENTS','PAYMENT_REQUESTS',N'Yêu cầu thanh toán','ti ti-qrcode','/payments',30),('INVOICE_GROUP','INVOICES','INVOICES',N'Hóa đơn','ti ti-receipt-2','/invoices',10),('BILLING_GROUP','CREDITS','CREDITS',N'Số dư Credit','ti ti-coins','/billing/credits',10),('INTEGRATION_GROUP','PROVIDERS','PROVIDERS',N'Nhà cung cấp','ti ti-plug-connected','/integrations/providers',10);
UPDATE dbo.Menus SET IsVisible=1 WHERE Code IN('BANKING_GROUP','INVOICE_GROUP','BILLING_GROUP','INTEGRATION_GROUP') AND IsDeleted=0;
UPDATE t SET ParentId=pm.Id,FunctionId=fn.Id,Name=s.Name,Icon=s.Icon,Route=s.Route,SortOrder=s.SortOrder,IsVisible=1,IsActive=1 FROM dbo.Menus t JOIN @N s ON s.Code=t.Code JOIN dbo.Menus pm ON pm.Code=s.ParentCode AND pm.IsDeleted=0 JOIN dbo.Functions fn ON fn.Code=s.FunctionCode AND fn.IsDeleted=0 WHERE t.IsDeleted=0;
INSERT dbo.Menus(ParentId,FunctionId,Code,Name,Icon,Route,SortOrder,IsVisible,IsActive) SELECT pm.Id,fn.Id,s.Code,s.Name,s.Icon,s.Route,s.SortOrder,1,1 FROM @N s JOIN dbo.Menus pm ON pm.Code=s.ParentCode AND pm.IsDeleted=0 JOIN dbo.Functions fn ON fn.Code=s.FunctionCode AND fn.IsDeleted=0 WHERE NOT EXISTS(SELECT 1 FROM dbo.Menus t WHERE t.Code=s.Code AND t.IsDeleted=0);
GO
INSERT dbo.MenuPermissions(MenuId,PermissionId) SELECT m.Id,p.Id FROM dbo.Menus m JOIN dbo.Permissions p ON p.Code=CASE m.Code WHEN 'PARTNERS' THEN 'PARTNER.PARTNERS.VIEW' WHEN 'BANK_ACCOUNTS' THEN 'BANKING.BANK_ACCOUNTS.VIEW' WHEN 'BANK_TRANSACTIONS' THEN 'BANKING.TRANSACTIONS.VIEW' WHEN 'PAYMENT_REQUESTS' THEN 'BANKING.PAYMENTS.VIEW' WHEN 'INVOICES' THEN 'INVOICE.INVOICES.VIEW' WHEN 'CREDITS' THEN 'BILLING.CREDITS.VIEW' WHEN 'PROVIDERS' THEN 'INTEGRATION.PROVIDERS.VIEW' END WHERE m.Code IN('PARTNERS','BANK_ACCOUNTS','BANK_TRANSACTIONS','PAYMENT_REQUESTS','INVOICES','CREDITS','PROVIDERS') AND NOT EXISTS(SELECT 1 FROM dbo.MenuPermissions mp WHERE mp.MenuId=m.Id AND mp.PermissionId=p.Id);
DECLARE @Admin uniqueidentifier=(SELECT TOP 1 Id FROM dbo.Roles WHERE Code='SYS_ADMIN' AND IsDeleted=0);
IF @Admin IS NOT NULL INSERT dbo.RolePermissions(RoleId,PermissionId) SELECT @Admin,p.Id FROM dbo.Permissions p WHERE p.IsDeleted=0 AND NOT EXISTS(SELECT 1 FROM dbo.RolePermissions rp WHERE rp.RoleId=@Admin AND rp.PermissionId=p.Id);
GO
PRINT '014-feature-navigation-permissions.sql: OK';
GO