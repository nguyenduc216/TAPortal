/* TAPORTAL-20261015-CRM-PHASE1-011D
   SQL Server 2014+, SSMS normal mode. SELECT/metadata only. No customer data output.
   Contract checks are NOT behavioral authorization or concurrency tests.
   Execute in TAPortalSys with metadata visibility; no SQLCMD or external files.
*/
SET NOCOUNT ON;
DECLARE @fail int=0, @name sysname, @column sysname, @kind varchar(20);
IF DB_NAME()<>N'TAPortalSys'
BEGIN
 PRINT 'FAIL database selector: expected TAPortalSys';
 SET @fail=@fail+1;
END
ELSE PRINT 'PASS database selector';

DECLARE @expected TABLE (ObjectName sysname NOT NULL, ColumnName sysname NOT NULL, TypeName sysname NULL);
INSERT @expected VALUES
 ('Users','Id','uniqueidentifier'),('Users','IsActive','bit'),('Users','IsDeleted','bit'),('Users','Status',NULL),
 ('Employees','Id','uniqueidentifier'),('Employees','UserId','uniqueidentifier'),('Employees','IsActive','bit'),('Employees','IsDeleted','bit'),('Employees','CurrentStatusId','uniqueidentifier'),('Employees','StartDate','date'),('Employees','EndDate','date'),
 ('EmployeeStatuses','Id','uniqueidentifier'),('EmployeeStatuses','Code',NULL),
 ('EmployeeAssignments','Id','uniqueidentifier'),('EmployeeAssignments','EmployeeId','uniqueidentifier'),('EmployeeAssignments','TeamId','uniqueidentifier'),('EmployeeAssignments','BranchId','uniqueidentifier'),('EmployeeAssignments','DepartmentId','uniqueidentifier'),('EmployeeAssignments','ValidFrom','date'),('EmployeeAssignments','ValidTo','date'),('EmployeeAssignments','IsPrimary','bit'),('EmployeeAssignments','IsCurrent','bit'),
 ('Customers','Id','uniqueidentifier'),('Customers','OwnerBranchId','uniqueidentifier'),('Customers','IsActive','bit'),('Customers','IsDeleted','bit'),('Customers','Status',NULL),
 ('FunctionActions','Id','uniqueidentifier'),('FunctionActions','UiKey',NULL),('FunctionActions','EndpointKey',NULL),('FunctionActions','IsActive','bit'),('FunctionActions','IsDeleted','bit'),
 ('FunctionActionPermissions','FunctionActionId','uniqueidentifier'),('FunctionActionPermissions','PermissionId','uniqueidentifier'),
 ('Permissions','Id','uniqueidentifier'),('Permissions','Code',NULL),('Permissions','IsActive','bit'),('Permissions','IsDeleted','bit'),
 ('UserPermissions','UserId','uniqueidentifier'),('UserPermissions','PermissionId','uniqueidentifier'),('UserPermissions','Effect',NULL),
 ('UserRoles','UserId','uniqueidentifier'),('UserRoles','RoleId','uniqueidentifier'),
 ('Roles','Id','uniqueidentifier'),('Roles','Code',NULL),('Roles','IsSystem','bit'),('Roles','IsActive','bit'),('Roles','IsDeleted','bit'),
 ('RolePermissions','RoleId','uniqueidentifier'),('RolePermissions','PermissionId','uniqueidentifier'),
 ('DataScopes','Id','uniqueidentifier'),('DataScopes','ScopeType',NULL),('DataScopes','IsActive','bit'),
 ('UserDataScopes','UserId','uniqueidentifier'),('UserDataScopes','DataScopeId','uniqueidentifier'),('UserDataScopes','FunctionCode',NULL),('UserDataScopes','ScopeReferenceId','uniqueidentifier'),('UserDataScopes','Effect',NULL),
 ('RoleDataScopes','RoleId','uniqueidentifier'),('RoleDataScopes','DataScopeId','uniqueidentifier'),('RoleDataScopes','FunctionCode',NULL),('RoleDataScopes','ScopeReferenceId','uniqueidentifier'),
 ('CustomerEmployeeAssignments','CustomerId','uniqueidentifier'),('CustomerEmployeeAssignments','EmployeeId','uniqueidentifier'),('CustomerEmployeeAssignments','IsActive','bit'),('CustomerEmployeeAssignments','ValidFrom','date'),('CustomerEmployeeAssignments','ValidTo','date'),
 ('CustomerServiceTeams','CustomerId','uniqueidentifier'),('CustomerServiceTeams','TeamId','uniqueidentifier'),('CustomerServiceTeams','IsActive','bit'),('CustomerServiceTeams','ValidFrom','date'),('CustomerServiceTeams','ValidTo','date'),
 ('CustomerBranchShares','CustomerId','uniqueidentifier'),('CustomerBranchShares','BranchId','uniqueidentifier'),('CustomerBranchShares','IsActive','bit'),('CustomerBranchShares','ValidFrom','date'),('CustomerBranchShares','ValidTo','date'),
 ('Branches','Id','uniqueidentifier'),('Branches','CompanyId','uniqueidentifier'),('Branches','IsActive','bit'),('Branches','IsDeleted','bit'),
 ('Companies','Id','uniqueidentifier'),('Companies','IsActive','bit'),('Companies','IsDeleted','bit'),
 ('Departments','Id','uniqueidentifier'),('Departments','BranchId','uniqueidentifier'),('Departments','IsActive','bit'),('Departments','IsDeleted','bit'),
 ('Teams','Id','uniqueidentifier'),('Teams','BranchId','uniqueidentifier'),('Teams','DepartmentId','uniqueidentifier'),('Teams','IsActive','bit'),('Teams','IsDeleted','bit');

DECLARE contract_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT ObjectName,ColumnName,TypeName FROM @expected;
OPEN contract_cursor;
FETCH NEXT FROM contract_cursor INTO @name,@column,@kind;
WHILE @@FETCH_STATUS=0
BEGIN
 IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
     WHERE c.object_id=OBJECT_ID(N'dbo.'+@name,N'U') AND c.name=@column AND (@kind IS NULL OR t.name=@kind))
 BEGIN
  PRINT 'FAIL contract dbo.'+@name+'.'+@column+COALESCE(' expected '+@kind,'');
  SET @fail=@fail+1;
 END
 FETCH NEXT FROM contract_cursor INTO @name,@column,@kind;
END
CLOSE contract_cursor;
DEALLOCATE contract_cursor;
IF @fail=0 PRINT 'PASS required object/column/type contracts';

-- Dynamic batch defers name binding until after metadata checks; missing tables
-- must yield FAIL messages rather than an SSMS batch compilation error.
IF OBJECT_ID(N'dbo.FunctionActions',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.FunctionActionPermissions',N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.Permissions',N'U') IS NOT NULL
 AND COL_LENGTH(N'dbo.FunctionActions',N'UiKey') IS NOT NULL
 AND COL_LENGTH(N'dbo.FunctionActions',N'EndpointKey') IS NOT NULL
 AND COL_LENGTH(N'dbo.FunctionActions',N'IsActive') IS NOT NULL
 AND COL_LENGTH(N'dbo.FunctionActions',N'IsDeleted') IS NOT NULL
BEGIN
 BEGIN TRY
  DECLARE @mappingMissing int=0;
  EXEC sys.sp_executesql N'SELECT @missing=COUNT(*) FROM (VALUES (N''Customers.Assign''),(N''Customers.Share''),(N''Customers.Team'')) a(k)
    WHERE NOT EXISTS (SELECT 1 FROM dbo.FunctionActions fa JOIN dbo.FunctionActionPermissions fap ON fap.FunctionActionId=fa.Id
      JOIN dbo.Permissions p ON p.Id=fap.PermissionId AND p.IsActive=1 AND p.IsDeleted=0
      WHERE fa.IsActive=1 AND fa.IsDeleted=0 AND (fa.UiKey=a.k OR fa.EndpointKey=a.k))',
    N'@missing int OUTPUT',@missing=@mappingMissing OUTPUT;
  IF @mappingMissing>0 BEGIN PRINT 'FAIL one or more action keys lack active mapped permissions'; SET @fail=@fail+1; END
  ELSE PRINT 'PASS active mapped action keys (no grants evaluated)';
 END TRY
 BEGIN CATCH
  PRINT 'FAIL action mapping inspection could not execute'; SET @fail=@fail+1;
 END CATCH
END
ELSE BEGIN PRINT 'FAIL cannot inspect action mappings: metadata incomplete'; SET @fail=@fail+1; END;

PRINT 'INFO SQL2014: syntax of this handoff uses SQL Server 2014 constructs; application SQL is not executed here';
PRINT 'UNVERIFIED grant resolution, ASSIGNED/TEAM/BRANCH/COMPANY joins, inclusive dates, HR eligibility, targeted DENY, SYS_ADMIN and Index-only grant behavior: no isolated fixtures';
PRINT 'NOT_RUN service-path SQL execution, transaction reuse and concurrent RBAC/HR/relationship changes';
IF @fail=0 PRINT 'VerificationStatus=PASS';
ELSE PRINT 'VerificationStatus=FAIL';