-- ADMIN-ROLE-PERMISSIONS - soft-delete the three test accounts, their employee records and the existing work task
-- (operator 2026-11-13: "xoa cac tai khoan cu (tru tai khoan admin) va cac task viec dang co"; the operator confirmed this
-- exact list in chat and approved that this migration updates existing rows). MIGRATION_NOT_EXECUTED.
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, after 036.
-- Scope (nothing else is touched; admin, its employee record and the customer accounts stay):
--   dbo.Users            Username TA_DEV_CRM_019_U1/U2/U3 (test fixtures)            -> IsDeleted=1
--   dbo.Employees        the records linked to those three users                       -> IsDeleted=1
--   dbo.WorkTasks        every task not yet deleted (one task on 2026-11-13)           -> IsDeleted=1
--   dbo.WorkTaskChecklistItems of those tasks                                          -> IsDeleted=1
-- Every row gets DeletedAt = 2026-11-13 00:37:00 (marker) and DeletedBy = the admin account, so the rollback restores
-- exactly these rows. Aborts when the rows found differ from the expected counts (3 users, 3 employees, 1 task).
-- Verify: database/scripts/063-verify-remove-test-accounts-and-tasks.sql. Rollback: database/scripts/064-rollback-remove-test-accounts-and-tasks.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @marker datetime2(3) = '2026-11-13T00:37:00';
DECLARE @admin uniqueidentifier = (SELECT TOP 1 u.Id FROM dbo.Users u JOIN dbo.UserRoles ur ON ur.UserId = u.Id JOIN dbo.Roles r ON r.Id = ur.RoleId
    WHERE u.Username = N'admin' AND u.IsDeleted = 0 AND r.Code = 'SYS_ADMIN');
DECLARE @users TABLE (Id uniqueidentifier PRIMARY KEY);
INSERT @users SELECT Id FROM dbo.Users WHERE Username IN (N'TA_DEV_CRM_019_U1', N'TA_DEV_CRM_019_U2', N'TA_DEV_CRM_019_U3') AND IsDeleted = 0;
DECLARE @employees TABLE (Id uniqueidentifier PRIMARY KEY);
INSERT @employees SELECT e.Id FROM dbo.Employees e JOIN @users u ON u.Id = e.UserId WHERE e.IsDeleted = 0;
DECLARE @tasks TABLE (Id uniqueidentifier PRIMARY KEY);
INSERT @tasks SELECT Id FROM dbo.WorkTasks WHERE IsDeleted = 0;
IF @admin IS NULL
    THROW 51001, 'ABORT: admin SYS_ADMIN account not found; no changes executed', 1;
IF (SELECT COUNT(1) FROM @users) <> 3 OR (SELECT COUNT(1) FROM @employees) <> 3 OR (SELECT COUNT(1) FROM @tasks) <> 1
    THROW 51002, 'ABORT: rows differ from the list the operator confirmed (3 users, 3 employees, 1 task); no changes executed', 1;
IF EXISTS (SELECT 1 FROM dbo.CustomerPortalMemberships m JOIN @users u ON u.Id = m.UserId)
    THROW 51003, 'ABORT: a test account is a customer portal member; no changes executed', 1;

UPDATE c SET IsDeleted = 1, DeletedAt = @marker, DeletedBy = @admin
FROM dbo.WorkTaskChecklistItems c JOIN @tasks t ON t.Id = c.TaskId WHERE c.IsDeleted = 0;
UPDATE w SET IsDeleted = 1, DeletedAt = @marker, DeletedBy = @admin, UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @admin
FROM dbo.WorkTasks w JOIN @tasks t ON t.Id = w.Id;
UPDATE e SET IsDeleted = 1, DeletedAt = @marker, DeletedBy = @admin, UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @admin
FROM dbo.Employees e JOIN @employees x ON x.Id = e.Id;
UPDATE u SET IsDeleted = 1, DeletedAt = @marker, DeletedBy = @admin, UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @admin
FROM dbo.Users u JOIN @users x ON x.Id = u.Id;
COMMIT;
PRINT N'037-remove-test-accounts-and-tasks.sql: OK';
