/* FEATURE_ID: TAPORTAL-HR-MASTER-DATA | CHANGE_ID: TAPORTAL-20261010-HR-MASTER-DATA-001 | READ ONLY */
SET NOCOUNT ON;
DECLARE @Pass int=0,@Warn int=0,@Fail int=0,@Count int=0;

IF DB_NAME()=N'TAPortalSys' BEGIN PRINT '[PASS] Database is TAPortalSys'; SET @Pass+=1; END ELSE BEGIN PRINT '[FAIL] Database is not TAPortalSys'; SET @Fail+=1; END;
IF OBJECT_ID(N'dbo.Departments',N'U') IS NOT NULL BEGIN PRINT '[PASS] dbo.Departments exists'; SET @Pass+=1; END ELSE BEGIN PRINT '[FAIL] dbo.Departments missing'; SET @Fail+=1; END;

IF OBJECT_ID(N'dbo.Departments',N'U') IS NOT NULL AND NOT EXISTS(
 SELECT 1 FROM (VALUES(N'Id'),(N'Code'),(N'Name'),(N'Description'),(N'SortOrder'),(N'IsActive'),(N'IsDeleted'),(N'CreatedAt'),(N'CreatedBy'),(N'UpdatedAt'),(N'UpdatedBy'),(N'DeletedAt'),(N'DeletedBy'),(N'RowVersion')) x(Name)
 WHERE COL_LENGTH(N'dbo.Departments',x.Name) IS NULL)
 BEGIN PRINT '[PASS] Department required columns exist'; SET @Pass+=1; END ELSE BEGIN PRINT '[FAIL] Department required columns missing'; SET @Fail+=1; END;

IF EXISTS(SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id WHERE c.object_id=OBJECT_ID(N'dbo.Departments') AND c.name=N'RowVersion' AND t.name IN(N'timestamp',N'rowversion') AND c.is_nullable=0)
 BEGIN PRINT '[PASS] Department RowVersion contract'; SET @Pass+=1; END ELSE BEGIN PRINT '[FAIL] Department RowVersion missing/incompatible'; SET @Fail+=1; END;

IF EXISTS(SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.Departments') AND i.is_unique=1 AND i.has_filter=1 AND i.filter_definition LIKE N'%IsDeleted%0%' AND EXISTS(SELECT 1 FROM sys.index_columns ic WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal=1 AND COL_NAME(ic.object_id,ic.column_id)=N'Code'))
 BEGIN PRINT '[PASS] Unique nondeleted Department.Code index'; SET @Pass+=1; END ELSE BEGIN PRINT '[FAIL] Unique nondeleted Department.Code index missing'; SET @Fail+=1; END;

IF EXISTS(SELECT 1 FROM dbo.Modules WHERE Code='HR' AND IsDeleted=0 AND IsActive=1) BEGIN PRINT '[PASS] HR Module active'; SET @Pass+=1; END ELSE BEGIN PRINT '[FAIL] HR Module inactive/missing'; SET @Fail+=1; END;
IF EXISTS(SELECT 1 FROM dbo.Functions f JOIN dbo.Modules m ON m.Id=f.ModuleId WHERE m.Code='HR' AND f.Code='DEPARTMENTS' AND f.IsDeleted=0 AND f.IsActive=1) BEGIN PRINT '[PASS] DEPARTMENTS Function active'; SET @Pass+=1; END ELSE BEGIN PRINT '[FAIL] DEPARTMENTS Function inactive/missing'; SET @Fail+=1; END;

SELECT @Count=COUNT(*) FROM dbo.Permissions WHERE Code IN('HR.DEPARTMENT.VIEW','HR.DEPARTMENT.CREATE','HR.DEPARTMENT.UPDATE','HR.DEPARTMENT.DELETE') AND IsDeleted=0 AND IsActive=1;
IF @Count=4 BEGIN PRINT '[PASS] Four Department permissions active'; SET @Pass+=1; END ELSE BEGIN PRINT '[FAIL] Four Department permissions not active'; SET @Fail+=1; END;

SELECT @Count=COUNT(*) FROM dbo.FunctionActions fa JOIN dbo.Functions f ON f.Id=fa.FunctionId JOIN dbo.Modules m ON m.Id=f.ModuleId WHERE m.Code='HR' AND f.Code='DEPARTMENTS' AND fa.Code IN('VIEW','CREATE','UPDATE','DELETE') AND fa.IsDeleted=0 AND fa.IsActive=1;
IF @Count=4 BEGIN PRINT '[PASS] Four Department FunctionActions active'; SET @Pass+=1; END ELSE BEGIN PRINT '[FAIL] Department FunctionActions incomplete'; SET @Fail+=1; END;

SELECT @Count=COUNT(*) FROM dbo.FunctionActionPermissions fap JOIN dbo.FunctionActions fa ON fa.Id=fap.FunctionActionId JOIN dbo.Functions f ON f.Id=fa.FunctionId JOIN dbo.Modules m ON m.Id=f.ModuleId JOIN dbo.Permissions p ON p.Id=fap.PermissionId WHERE m.Code='HR' AND f.Code='DEPARTMENTS' AND ((fa.Code='VIEW' AND p.Code='HR.DEPARTMENT.VIEW') OR (fa.Code='CREATE' AND p.Code='HR.DEPARTMENT.CREATE') OR (fa.Code='UPDATE' AND p.Code='HR.DEPARTMENT.UPDATE') OR (fa.Code='DELETE' AND p.Code='HR.DEPARTMENT.DELETE'));
IF @Count=4 BEGIN PRINT '[PASS] Four action-permission mappings'; SET @Pass+=1; END ELSE BEGIN PRINT '[FAIL] Action-permission mappings incomplete'; SET @Fail+=1; END;

IF EXISTS(SELECT 1 FROM dbo.Menus c JOIN dbo.Menus p ON p.Id=c.ParentId WHERE c.Code='HR_DEPARTMENTS' AND c.Route=N'/hr/departments' AND c.IsDeleted=0 AND c.IsActive=1 AND c.IsVisible=1 AND p.Code='HR' AND p.IsDeleted=0) BEGIN PRINT '[PASS] Department menu under HR'; SET @Pass+=1; END ELSE BEGIN PRINT '[FAIL] Department menu missing/not under HR'; SET @Fail+=1; END;
IF EXISTS(SELECT 1 FROM dbo.Menus m JOIN dbo.MenuPermissions mp ON mp.MenuId=m.Id JOIN dbo.Permissions p ON p.Id=mp.PermissionId WHERE m.Code='HR_DEPARTMENTS' AND m.IsDeleted=0 AND p.Code='HR.DEPARTMENT.VIEW' AND p.IsDeleted=0) BEGIN PRINT '[PASS] Department menu maps VIEW permission'; SET @Pass+=1; END ELSE BEGIN PRINT '[FAIL] Department menu VIEW mapping missing'; SET @Fail+=1; END;

SELECT @Count=COUNT(*) FROM dbo.RolePermissions rp JOIN dbo.Roles r ON r.Id=rp.RoleId JOIN dbo.Permissions p ON p.Id=rp.PermissionId WHERE r.Code='SYS_ADMIN' AND r.IsDeleted=0 AND r.IsActive=1 AND p.Code IN('HR.DEPARTMENT.VIEW','HR.DEPARTMENT.CREATE','HR.DEPARTMENT.UPDATE','HR.DEPARTMENT.DELETE') AND p.IsDeleted=0;
IF @Count=4 BEGIN PRINT '[PASS] SYS_ADMIN has four Department permissions'; SET @Pass+=1; END ELSE BEGIN PRINT '[FAIL] SYS_ADMIN Department grants incomplete'; SET @Fail+=1; END;

IF OBJECT_ID(N'dbo.Departments',N'U') IS NOT NULL BEGIN EXEC sys.sp_executesql N'SELECT @n=COUNT(*) FROM (SELECT Code FROM dbo.Departments WHERE IsDeleted=0 GROUP BY Code HAVING COUNT(*)>1)d;',N'@n int OUTPUT',@Count OUTPUT; END ELSE SET @Count=1;
IF @Count=0 BEGIN PRINT '[PASS] No duplicate nondeleted Department.Code'; SET @Pass+=1; END ELSE BEGIN PRINT '[FAIL] Duplicate nondeleted Department.Code'; SET @Fail+=1; END;

PRINT 'TAPortalSys HR DEPARTMENT VERIFY SUMMARY';
PRINT 'PASS: '+CONVERT(varchar(12),@Pass); PRINT 'WARN: '+CONVERT(varchar(12),@Warn); PRINT 'FAIL: '+CONVERT(varchar(12),@Fail);
IF @Fail>0 BEGIN PRINT 'OVERALL STATUS: FAIL'; RAISERROR('HR Department verification failed.',16,1); END
ELSE IF @Warn>0 PRINT 'OVERALL STATUS: PASS_WITH_WARNINGS'; ELSE PRINT 'OVERALL STATUS: PASS';
GO
