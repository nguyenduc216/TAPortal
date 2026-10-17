param([switch]$Provision)
$ErrorActionPreference = 'Stop'
if (-not $Provision) { throw 'Explicit -Provision required.' }
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$config = Get-Content (Join-Path $root 'src/TAPortal.Web/appsettings.json') -Raw | ConvertFrom-Json
$cn = New-Object System.Data.SqlClient.SqlConnection([string]$config.Database.ConnectionString)
try {
 $cn.Open()
 if ($cn.Database -ne 'TAPortalSys') { throw 'Database identity mismatch.' }
 $tx = $cn.BeginTransaction([System.Data.IsolationLevel]::Serializable)
 try {
  $cmd = $cn.CreateCommand(); $cmd.Transaction = $tx
  $cmd.CommandText = @'
DECLARE @admin uniqueidentifier='3894fe22-4ccd-4dd5-8d84-c1c610022ae2',
 @employee uniqueidentifier='02000000-0000-4000-8000-000000000021',
 @assignment uniqueidentifier='02000000-0000-4000-8000-000000000031',
 @branch uniqueidentifier='01900000-0000-4000-8000-000000000002',
 @department uniqueidentifier='01900000-0000-4000-8000-000000000004',
 @team uniqueidentifier='01900000-0000-4000-8000-000000000005';
IF NOT EXISTS (SELECT 1 FROM dbo.Users u WITH (UPDLOCK,HOLDLOCK) JOIN dbo.UserRoles ur ON ur.UserId=u.Id JOIN dbo.Roles r ON r.Id=ur.RoleId WHERE u.Id=@admin AND u.IsActive=1 AND u.IsDeleted=0 AND u.Status='ACTIVE' AND r.Code='SYS_ADMIN' AND r.IsSystem=1 AND r.IsActive=1 AND r.IsDeleted=0)
 THROW 51020,'Admin identity/role mismatch',1;
IF NOT EXISTS (SELECT 1 FROM dbo.Branches b JOIN dbo.Companies c ON c.Id=b.CompanyId JOIN dbo.Departments d ON d.BranchId=b.Id JOIN dbo.Teams t ON t.BranchId=b.Id AND t.DepartmentId=d.Id WHERE b.Id=@branch AND b.Code='TA_DEV_CRM_019_B1' AND d.Id=@department AND d.Code='TA_DEV_CRM_019_D' AND t.Id=@team AND t.Code='TA_DEV_CRM_019_T1' AND b.IsActive=1 AND b.IsDeleted=0 AND c.IsActive=1 AND c.IsDeleted=0 AND d.IsActive=1 AND d.IsDeleted=0 AND t.IsActive=1 AND t.IsDeleted=0)
 THROW 51020,'HR fixture mismatch',1;
IF EXISTS (SELECT 1 FROM dbo.Employees WITH (UPDLOCK,HOLDLOCK) WHERE UserId=@admin AND Id<>@employee)
 THROW 51020,'Admin already linked to another employee',1;
IF EXISTS (SELECT 1 FROM dbo.Employees WHERE EmployeeCode='TA_DEV_CRM_020_ADMIN' AND (Id<>@employee OR UserId<>@admin)) OR EXISTS (SELECT 1 FROM dbo.Employees WHERE Id=@employee AND (EmployeeCode<>'TA_DEV_CRM_020_ADMIN' OR UserId<>@admin))
 THROW 51020,'Employee fixture collision',1;
IF EXISTS (SELECT 1 FROM dbo.EmployeeAssignments WHERE EmployeeId=@employee AND Id<>@assignment) OR EXISTS (SELECT 1 FROM dbo.EmployeeAssignments WHERE Id=@assignment AND (EmployeeId<>@employee OR BranchId<>@branch OR DepartmentId<>@department OR TeamId<>@team))
 THROW 51020,'Assignment fixture collision',1;
IF NOT EXISTS (SELECT 1 FROM dbo.Employees WHERE Id=@employee)
 INSERT dbo.Employees(Id,EmployeeCode,UserId,CurrentStatusId,FullName,StartDate)
 SELECT @employee,'TA_DEV_CRM_020_ADMIN',@admin,s.Id,N'TA_DEV_CRM_020 admin HR fixture',DATEADD(day,-10,CONVERT(date,GETDATE())) FROM dbo.EmployeeStatuses s WHERE s.Code='ACTIVE' AND s.IsActive=1;
IF NOT EXISTS (SELECT 1 FROM dbo.Employees WHERE Id=@employee AND IsActive=1 AND IsDeleted=0 AND (EndDate IS NULL OR EndDate>=CONVERT(date,GETDATE()))) THROW 51020,'Employee ineligible',1;
IF NOT EXISTS (SELECT 1 FROM dbo.EmployeeAssignments WHERE Id=@assignment)
 INSERT dbo.EmployeeAssignments(Id,EmployeeId,BranchId,DepartmentId,TeamId,ValidFrom,IsPrimary,IsCurrent)
 VALUES(@assignment,@employee,@branch,@department,@team,DATEADD(day,-10,CONVERT(date,GETDATE())),1,1);
IF NOT EXISTS (SELECT 1 FROM dbo.EmployeeAssignments WHERE Id=@assignment AND IsPrimary=1 AND IsCurrent=1 AND ValidFrom<=CONVERT(date,GETDATE()) AND (ValidTo IS NULL OR ValidTo>=CONVERT(date,GETDATE()))) THROW 51020,'Assignment ineligible',1;
'@
  [void]$cmd.ExecuteNonQuery(); $cmd.Dispose(); $tx.Commit()
  'ADMIN_HR_LINK=PASS; DATABASE=TAPortalSys; EMPLOYEE=02000000-0000-4000-8000-000000000021'
 } catch { $tx.Rollback(); throw } finally { $tx.Dispose() }
} finally { $cn.Dispose() }