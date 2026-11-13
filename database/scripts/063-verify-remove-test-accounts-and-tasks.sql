-- ADMIN-ROLE-PERMISSIONS - READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/037-remove-test-accounts-and-tasks.sql. Expected last line: VERIFY_STATUS=PASS (NOT_APPLIED before the migration).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
DECLARE @marker datetime2(3) = '2026-11-13T00:37:00';
DECLARE @liveUsers int = (SELECT COUNT(1) FROM dbo.Users WHERE Username IN (N'TA_DEV_CRM_019_U1', N'TA_DEV_CRM_019_U2', N'TA_DEV_CRM_019_U3') AND IsDeleted = 0);
DECLARE @markedUsers int = (SELECT COUNT(1) FROM dbo.Users WHERE IsDeleted = 1 AND DeletedAt = @marker);
DECLARE @markedEmployees int = (SELECT COUNT(1) FROM dbo.Employees WHERE IsDeleted = 1 AND DeletedAt = @marker);
DECLARE @markedTasks int = (SELECT COUNT(1) FROM dbo.WorkTasks WHERE IsDeleted = 1 AND DeletedAt = @marker);
DECLARE @adminOk int = (SELECT COUNT(1) FROM dbo.Users u JOIN dbo.UserRoles ur ON ur.UserId = u.Id JOIN dbo.Roles r ON r.Id = ur.RoleId
    WHERE u.Username = N'admin' AND u.IsDeleted = 0 AND u.IsActive = 1 AND r.Code = 'SYS_ADMIN');
PRINT N'INFO: test users not deleted = ' + CAST(@liveUsers AS nvarchar(10)) + N'; marked users/employees/tasks = '
    + CAST(@markedUsers AS nvarchar(10)) + N'/' + CAST(@markedEmployees AS nvarchar(10)) + N'/' + CAST(@markedTasks AS nvarchar(10))
    + N'; admin active = ' + CAST(@adminOk AS nvarchar(10));
IF @markedUsers = 0 AND @markedEmployees = 0 AND @markedTasks = 0 AND @liveUsers = 3 BEGIN PRINT N'VERIFY_STATUS=NOT_APPLIED'; RETURN; END
IF @liveUsers = 0 AND @markedUsers = 3 AND @markedEmployees = 3 AND @markedTasks = 1 AND @adminOk = 1
    PRINT N'PASS: test accounts, their employee records and the old task are soft-deleted; admin is active';
PRINT N'VERIFY_STATUS=' + CASE WHEN @liveUsers = 0 AND @markedUsers = 3 AND @markedEmployees = 3 AND @markedTasks = 1 AND @adminOk = 1 THEN N'PASS' ELSE N'FAIL' END;
