-- CONTRACTS-AND-TASKS - READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/043-work-task-catalog.sql. Expected last line: VERIFY_STATUS=PASS (NOT_APPLIED before the migration).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
IF OBJECT_ID(N'dbo.WorkTaskCatalog', N'U') IS NULL BEGIN PRINT N'INFO: dbo.WorkTaskCatalog missing'; PRINT N'VERIFY_STATUS=NOT_APPLIED'; RETURN; END
DECLARE @fail int = 0;
IF COL_LENGTH(N'dbo.WorkTaskCatalog', N'Name') IS NULL OR COL_LENGTH(N'dbo.WorkTaskCatalog', N'IsActive') IS NULL
    BEGIN PRINT N'FAIL: WorkTaskCatalog columns missing'; SET @fail += 1; END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_WorkTaskCatalog_Name' AND object_id = OBJECT_ID(N'dbo.WorkTaskCatalog') AND is_unique = 1 AND has_filter = 1)
    BEGIN PRINT N'FAIL: UX_WorkTaskCatalog_Name missing'; SET @fail += 1; END
IF NOT EXISTS (SELECT 1 FROM dbo.Permissions p JOIN dbo.RolePermissions rp ON rp.PermissionId = p.Id JOIN dbo.Roles r ON r.Id = rp.RoleId
               WHERE p.Code = 'TASKS.CATALOG.MANAGE' AND p.IsDeleted = 0 AND r.Code = 'SYS_ADMIN')
    BEGIN PRINT N'FAIL: TASKS.CATALOG.MANAGE not granted to SYS_ADMIN'; SET @fail += 1; END
IF @fail = 0 PRINT N'PASS: task catalogue table, unique name index and permission';
PRINT N'VERIFY_STATUS=' + CASE WHEN @fail = 0 THEN N'PASS' ELSE N'FAIL' END;
