-- CONTRACTS-AND-TASKS - READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/031-contracts-and-work-tasks.sql. Expected last line: VERIFY_STATUS=PASS (NOT_APPLIED before the migration).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
DECLARE @fail int = 0;
PRINT N'=== CONTRACTS AND WORK TASKS VERIFY ===';
DECLARE @Tables TABLE (Name sysname NOT NULL PRIMARY KEY);
INSERT @Tables (Name) VALUES (N'CustomerContracts'), (N'CustomerContractFiles'), (N'WorkTaskTemplates'), (N'WorkTasks'),
 (N'WorkTaskMembers'), (N'WorkTaskChecklistItems'), (N'WorkTaskActivities');
DECLARE @tableCount int = (SELECT COUNT(1) FROM @Tables t WHERE OBJECT_ID(N'dbo.' + t.Name, N'U') IS NOT NULL);
DECLARE @Marker uniqueidentifier = 'A0000031-0000-4000-8000-000000000031';
DECLARE @Catalogue TABLE (Code varchar(200) COLLATE DATABASE_DEFAULT NOT NULL PRIMARY KEY);
INSERT @Catalogue (Code) VALUES
 ('CONTRACTS.VIEW'), ('CONTRACTS.CREATE'), ('CONTRACTS.UPDATE'), ('CONTRACTS.DELETE'), ('TASKS.VIEW'), ('TASKS.CREATE'),
 ('TASKS.ASSIGN'), ('TASKS.VIEW_ALL'), ('TASKS.TEMPLATES.MANAGE'), ('TASKS.REPORT.VIEW');
DECLARE @total int = (SELECT COUNT(1) FROM @Catalogue);
DECLARE @present int = (SELECT COUNT(DISTINCT c.Code) FROM @Catalogue c JOIN dbo.Permissions p ON p.Code = c.Code AND p.IsDeleted = 0);
DECLARE @sequence int = (SELECT COUNT(1) FROM dbo.NumberSequences WHERE Code = 'TASK_CODE' AND ScopeId IS NULL AND IsActive = 1);
PRINT N'INFO: tables present = ' + CAST(@tableCount AS nvarchar(10)) + N'/7, permission codes present = ' + CAST(@present AS nvarchar(10))
    + N'/' + CAST(@total AS nvarchar(10)) + N', TASK_CODE sequence = ' + CAST(@sequence AS nvarchar(10));
IF @tableCount = 0 AND @present = 0 AND @sequence = 0
BEGIN
    PRINT N'VERIFY_STATUS=NOT_APPLIED';
    RETURN;
END
IF @tableCount = 7 PRINT N'PASS: all 7 tables exist'; ELSE BEGIN PRINT N'FAIL: tables missing'; SET @fail += 1; END
IF @present = @total PRINT N'PASS: every CONTRACTS/TASKS permission exists'; ELSE BEGIN PRINT N'FAIL: permission codes missing'; SET @fail += 1; END
IF @sequence = 1 PRINT N'PASS: TASK_CODE sequence exists'; ELSE BEGIN PRINT N'FAIL: TASK_CODE sequence missing or duplicated'; SET @fail += 1; END
DECLARE @SysAdmin uniqueidentifier = (SELECT TOP 1 Id FROM dbo.Roles WHERE Code = 'SYS_ADMIN' AND IsDeleted = 0);
DECLARE @granted int = (SELECT COUNT(DISTINCT c.Code) FROM @Catalogue c JOIN dbo.Permissions p ON p.Code = c.Code AND p.IsDeleted = 0
                        JOIN dbo.RolePermissions rp ON rp.PermissionId = p.Id AND rp.RoleId = @SysAdmin);
IF @granted = @total PRINT N'PASS: SYS_ADMIN holds every new permission'; ELSE BEGIN PRINT N'FAIL: SYS_ADMIN lacks new permissions'; SET @fail += 1; END
IF @tableCount = 7
BEGIN
    IF COL_LENGTH(N'dbo.WorkTasks', N'LastActivityAt') IS NOT NULL AND COL_LENGTH(N'dbo.WorkTasks', N'OwnerEmployeeId') IS NOT NULL
       AND COL_LENGTH(N'dbo.WorkTaskChecklistItems', N'DoneAt') IS NOT NULL AND COL_LENGTH(N'dbo.WorkTaskTemplates', N'ChecklistText') IS NOT NULL
        PRINT N'PASS: key columns exist';
    ELSE BEGIN PRINT N'FAIL: key columns missing'; SET @fail += 1; END
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.WorkTasks') AND name = N'UX_WorkTasks_TemplatePeriod' AND is_unique = 1)
       AND EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.CustomerContracts') AND name = N'UX_CustomerContracts_ContractNo' AND is_unique = 1)
        PRINT N'PASS: unique indexes exist';
    ELSE BEGIN PRINT N'FAIL: unique indexes missing'; SET @fail += 1; END
END
IF EXISTS (SELECT 1 FROM dbo.Permissions WHERE CreatedBy = @Marker AND Code NOT IN (SELECT Code FROM @Catalogue))
    BEGIN PRINT N'FAIL: marker rows outside the catalogue'; SET @fail += 1; END
ELSE PRINT N'PASS: migration rows limited to the catalogue';
PRINT N'VERIFY_STATUS=' + CASE WHEN @fail = 0 THEN N'PASS' ELSE N'FAIL' END;
