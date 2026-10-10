/* Read-only verification for TAPORTAL-20261010-HR-MASTER-DATA-002. */
SET NOCOUNT ON;
DECLARE @Results TABLE(Id int IDENTITY(1,1),Status varchar(5),CheckName nvarchar(200),Detail nvarchar(500));
IF DB_NAME()<>N'TAPortalSys' BEGIN INSERT @Results VALUES('FAIL',N'01 Database',N'Expected TAPortalSys; actual '+DB_NAME()); GOTO Finish; END
INSERT @Results VALUES('PASS',N'01 Database',DB_NAME());
INSERT @Results SELECT CASE WHEN OBJECT_ID(N'dbo.Departments',N'U') IS NOT NULL THEN 'PASS' ELSE 'FAIL' END,N'02 Departments table',N'Required owner';
INSERT @Results SELECT CASE WHEN OBJECT_ID(N'dbo.Teams',N'U') IS NOT NULL THEN 'PASS' ELSE 'FAIL' END,N'03 Teams table',N'Team master';
IF OBJECT_ID(N'dbo.Teams',N'U') IS NULL GOTO Finish;
INSERT @Results SELECT CASE WHEN (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Teams') AND name IN(N'Id',N'DepartmentId',N'ParentTeamId',N'Code',N'Name',N'Description',N'SortOrder',N'IsActive',N'IsDeleted',N'CreatedAt',N'CreatedBy',N'UpdatedAt',N'UpdatedBy',N'DeletedAt',N'DeletedBy',N'RowVersion'))=16 THEN 'PASS' ELSE 'FAIL' END,N'04 Required columns',N'Expected 16 logical columns';
INSERT @Results SELECT CASE WHEN EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Teams') AND name=N'RowVersion' AND system_type_id=189) THEN 'PASS' ELSE 'FAIL' END,N'05 RowVersion',N'timestamp/rowversion';
INSERT @Results SELECT CASE WHEN EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.Teams') AND name=N'FK_Teams_Departments' AND is_disabled=0 AND is_not_trusted=0) THEN 'PASS' ELSE 'FAIL' END,N'06 Department FK',N'Enabled and trusted';
INSERT @Results SELECT CASE WHEN EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.Teams') AND name=N'FK_Teams_ParentTeam' AND is_disabled=0 AND is_not_trusted=0) THEN 'PASS' ELSE 'FAIL' END,N'07 Parent FK',N'Enabled and trusted';
INSERT @Results SELECT CASE WHEN EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Teams') AND name=N'UX_Teams_Code_NotDeleted' AND is_unique=1 AND has_filter=1) THEN 'PASS' ELSE 'FAIL' END,N'08 Unique active Code',N'Filtered unique index';
INSERT @Results SELECT CASE WHEN EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Teams') AND name=N'IX_Teams_DepartmentId') THEN 'PASS' ELSE 'FAIL' END,N'09 Department index',N'Lookup index';
INSERT @Results SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.Modules WHERE Code='HR' AND IsDeleted=0 AND IsActive=1) THEN 'PASS' ELSE 'FAIL' END,N'10 HR module',N'Active';
INSERT @Results SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.Functions WHERE Code='TEAMS' AND Route=N'/hr/teams' AND IsDeleted=0 AND IsActive=1) THEN 'PASS' ELSE 'FAIL' END,N'11 TEAMS function',N'Active route';
INSERT @Results SELECT CASE WHEN (SELECT COUNT(*) FROM dbo.Permissions WHERE Code IN('HR.TEAM.VIEW','HR.TEAM.CREATE','HR.TEAM.UPDATE','HR.TEAM.DELETE') AND IsDeleted=0 AND IsActive=1)=4 THEN 'PASS' ELSE 'FAIL' END,N'12 Team permissions',N'Four active permissions';
INSERT @Results SELECT CASE WHEN (SELECT COUNT(*) FROM dbo.FunctionActions fa JOIN dbo.Functions f ON f.Id=fa.FunctionId WHERE f.Code='TEAMS' AND fa.EndpointKey IN('Teams.Index','Teams.Create','Teams.Update','Teams.Delete') AND fa.IsDeleted=0 AND fa.IsActive=1)=4 THEN 'PASS' ELSE 'FAIL' END,N'13 Team actions',N'Four active actions';
INSERT @Results SELECT CASE WHEN (SELECT COUNT(DISTINCT fa.EndpointKey) FROM dbo.FunctionActionPermissions fap JOIN dbo.FunctionActions fa ON fa.Id=fap.FunctionActionId JOIN dbo.Permissions p ON p.Id=fap.PermissionId WHERE fa.EndpointKey IN('Teams.Index','Teams.Create','Teams.Update','Teams.Delete') AND p.Code IN('HR.TEAM.VIEW','HR.TEAM.CREATE','HR.TEAM.UPDATE','HR.TEAM.DELETE'))=4 THEN 'PASS' ELSE 'FAIL' END,N'14 Action mappings',N'Four action-permission mappings';
INSERT @Results SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.Menus WHERE Code='HR_TEAMS' AND Route=N'/hr/teams' AND IsDeleted=0 AND IsActive=1 AND IsVisible=1) THEN 'PASS' ELSE 'FAIL' END,N'15 Team menu',N'Visible and active';
INSERT @Results SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.Menus m JOIN dbo.MenuPermissions mp ON mp.MenuId=m.Id JOIN dbo.Permissions p ON p.Id=mp.PermissionId WHERE m.Code='HR_TEAMS' AND p.Code='HR.TEAM.VIEW') THEN 'PASS' ELSE 'FAIL' END,N'16 Menu permission',N'HR.TEAM.VIEW';
INSERT @Results SELECT CASE WHEN (SELECT COUNT(DISTINCT p.Code) FROM dbo.Roles r JOIN dbo.RolePermissions rp ON rp.RoleId=r.Id JOIN dbo.Permissions p ON p.Id=rp.PermissionId WHERE r.Code='SYS_ADMIN' AND r.IsDeleted=0 AND r.IsActive=1 AND p.Code IN('HR.TEAM.VIEW','HR.TEAM.CREATE','HR.TEAM.UPDATE','HR.TEAM.DELETE'))=4 THEN 'PASS' ELSE 'FAIL' END,N'17 SYS_ADMIN grants',N'Four RolePermissions';
INSERT @Results SELECT CASE WHEN NOT EXISTS(SELECT 1 FROM dbo.Teams t LEFT JOIN dbo.Departments d ON d.Id=t.DepartmentId WHERE d.Id IS NULL OR d.IsDeleted=1) THEN 'PASS' ELSE 'FAIL' END,N'18 Department integrity',N'No orphan/deleted owner';
INSERT @Results SELECT CASE WHEN NOT EXISTS(SELECT 1 FROM dbo.Teams t LEFT JOIN dbo.Teams p ON p.Id=t.ParentTeamId WHERE t.ParentTeamId IS NOT NULL AND (p.Id IS NULL OR p.IsDeleted=1)) THEN 'PASS' ELSE 'FAIL' END,N'19 Parent integrity',N'No orphan/deleted parent';
INSERT @Results SELECT CASE WHEN NOT EXISTS(SELECT 1 FROM dbo.Teams t JOIN dbo.Teams p ON p.Id=t.ParentTeamId WHERE t.DepartmentId<>p.DepartmentId) THEN 'PASS' ELSE 'FAIL' END,N'20 Same Department',N'No cross-department parent';
INSERT @Results SELECT CASE WHEN NOT EXISTS(SELECT 1 FROM dbo.Teams WHERE Id=ParentTeamId) THEN 'PASS' ELSE 'FAIL' END,N'21 Self parent',N'No self-parent';
INSERT @Results SELECT CASE WHEN NOT EXISTS(SELECT Code FROM dbo.Teams WHERE IsDeleted=0 GROUP BY Code HAVING COUNT(*)>1) THEN 'PASS' ELSE 'FAIL' END,N'22 Unique Code data',N'No duplicate nondeleted Code';
DECLARE @Cycle bit=0;
;WITH Walk AS(SELECT Id,ParentTeamId,CAST('/'+CONVERT(varchar(36),Id)+'/' AS varchar(max)) Path,CAST(0 AS bit) Cycle FROM dbo.Teams WHERE IsDeleted=0 UNION ALL SELECT w.Id,p.ParentTeamId,CAST(w.Path+CONVERT(varchar(36),p.Id)+'/' AS varchar(max)),CAST(CASE WHEN CHARINDEX('/'+CONVERT(varchar(36),p.Id)+'/',w.Path)>0 THEN 1 ELSE 0 END AS bit) FROM Walk w JOIN dbo.Teams p ON p.Id=w.ParentTeamId AND p.IsDeleted=0 WHERE w.Cycle=0) SELECT @Cycle=1 FROM Walk WHERE Cycle=1 OPTION(MAXRECURSION 32767);
INSERT @Results VALUES(CASE WHEN @Cycle=0 THEN 'PASS' ELSE 'FAIL' END,N'23 Hierarchy cycle',N'No recursive cycle');
Finish:
SELECT Status,CheckName,Detail FROM @Results ORDER BY Id;
DECLARE @Pass int=(SELECT COUNT(*) FROM @Results WHERE Status='PASS'),@Warn int=(SELECT COUNT(*) FROM @Results WHERE Status='WARN'),@Fail int=(SELECT COUNT(*) FROM @Results WHERE Status='FAIL');
PRINT 'PASS: '+CONVERT(varchar(10),@Pass); PRINT 'WARN: '+CONVERT(varchar(10),@Warn); PRINT 'FAIL: '+CONVERT(varchar(10),@Fail); PRINT 'OVERALL STATUS: '+CASE WHEN @Fail>0 THEN 'FAIL' WHEN @Warn>0 THEN 'PASS_WITH_WARNINGS' ELSE 'PASS' END;
IF @Fail>0 RAISERROR('056 HR Team verification failed.',16,1);
GO
