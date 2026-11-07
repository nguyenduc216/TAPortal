-- CONTRACTS-AND-TASKS - READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/032-sample-work-roles.sql. Expected last line: VERIFY_STATUS=PASS (NOT_APPLIED before the migration).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
DECLARE @fail int = 0;
DECLARE @Marker uniqueidentifier = 'A0000032-0000-4000-8000-000000000032';
PRINT N'=== SAMPLE WORK ROLES VERIFY ===';
DECLARE @roles int = (SELECT COUNT(1) FROM dbo.Roles WHERE Code IN ('STAFF','MANAGER') AND IsDeleted = 0);
DECLARE @markerRoles int = (SELECT COUNT(1) FROM dbo.Roles WHERE CreatedBy = @Marker);
DECLARE @markerGrants int = (SELECT COUNT(1) FROM dbo.RolePermissions WHERE CreatedBy = @Marker);
PRINT N'INFO: roles STAFF/MANAGER present = ' + CAST(@roles AS nvarchar(10)) + N', added by 032 = ' + CAST(@markerRoles AS nvarchar(10))
    + N', grants added by 032 = ' + CAST(@markerGrants AS nvarchar(10));
IF @markerRoles = 0 AND @markerGrants = 0
BEGIN
    PRINT N'VERIFY_STATUS=NOT_APPLIED';
    RETURN;
END
IF @roles = 2 PRINT N'PASS: roles STAFF and MANAGER exist'; ELSE BEGIN PRINT N'FAIL: role missing'; SET @fail += 1; END
DECLARE @staff int = (SELECT COUNT(1) FROM dbo.RolePermissions rp JOIN dbo.Roles r ON r.Id = rp.RoleId WHERE r.Code = 'STAFF' AND rp.CreatedBy = @Marker);
DECLARE @manager int = (SELECT COUNT(1) FROM dbo.RolePermissions rp JOIN dbo.Roles r ON r.Id = rp.RoleId WHERE r.Code = 'MANAGER' AND rp.CreatedBy = @Marker);
IF @markerRoles = 2 AND (@staff <> 2 OR @manager <> 10) BEGIN PRINT N'FAIL: grant count STAFF=' + CAST(@staff AS nvarchar(10)) + N' MANAGER=' + CAST(@manager AS nvarchar(10)); SET @fail += 1; END
ELSE PRINT N'PASS: grants STAFF=' + CAST(@staff AS nvarchar(10)) + N', MANAGER=' + CAST(@manager AS nvarchar(10));
IF EXISTS (SELECT 1 FROM dbo.RolePermissions rp JOIN dbo.Roles r ON r.Id = rp.RoleId WHERE rp.CreatedBy = @Marker AND r.Code NOT IN ('STAFF','MANAGER'))
    BEGIN PRINT N'FAIL: marker grants on another role'; SET @fail += 1; END
ELSE PRINT N'PASS: grants limited to the sample roles';
PRINT N'VERIFY_STATUS=' + CASE WHEN @fail = 0 THEN N'PASS' ELSE N'FAIL' END;
