-- CONTRACTS-AND-TASKS - ROLLBACK of migrations/031-contracts-and-work-tasks.sql.
-- Drops the 7 tables created by 031 and removes the TASK_CODE sequence and the permission rows/grants 031 inserted
-- (CreatedBy = 'A0000031-0000-4000-8000-000000000031'; rows that existed before 031 are never touched).
-- Refuses when any contract, file, template or task exists (data would be lost: decide with the operator first), or when
-- a role other than SYS_ADMIN, a direct user permission or a menu uses a permission added by 031.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @Marker uniqueidentifier = 'A0000031-0000-4000-8000-000000000031';
DECLARE @rows int = 0, @n int;
IF OBJECT_ID(N'dbo.CustomerContracts', N'U') IS NOT NULL BEGIN EXEC sp_executesql N'SELECT @n = COUNT(1) FROM dbo.CustomerContracts', N'@n int OUTPUT', @n = @n OUTPUT; SET @rows += @n; END
IF OBJECT_ID(N'dbo.WorkTaskTemplates', N'U') IS NOT NULL BEGIN EXEC sp_executesql N'SELECT @n = COUNT(1) FROM dbo.WorkTaskTemplates', N'@n int OUTPUT', @n = @n OUTPUT; SET @rows += @n; END
IF OBJECT_ID(N'dbo.WorkTasks', N'U') IS NOT NULL BEGIN EXEC sp_executesql N'SELECT @n = COUNT(1) FROM dbo.WorkTasks', N'@n int OUTPUT', @n = @n OUTPUT; SET @rows += @n; END
IF @rows > 0
    THROW 51002, 'Rollback refused: contracts, templates or tasks exist (data would be lost)', 1;
IF EXISTS (SELECT 1 FROM dbo.RolePermissions rp JOIN dbo.Permissions p ON p.Id = rp.PermissionId JOIN dbo.Roles r ON r.Id = rp.RoleId
           WHERE p.CreatedBy = @Marker AND r.Code <> 'SYS_ADMIN')
    THROW 51003, 'Rollback refused: a role other than SYS_ADMIN uses a permission added by migration 031', 1;
IF OBJECT_ID(N'dbo.UserPermissions', N'U') IS NOT NULL
   AND EXISTS (SELECT 1 FROM dbo.UserPermissions up JOIN dbo.Permissions p ON p.Id = up.PermissionId WHERE p.CreatedBy = @Marker)
    THROW 51004, 'Rollback refused: a direct user permission uses a permission added by migration 031', 1;
DECLARE @menuRefs int = 0;
IF OBJECT_ID(N'dbo.MenuPermissions', N'U') IS NOT NULL
    EXEC sp_executesql N'SELECT @n = COUNT(1) FROM dbo.MenuPermissions mp JOIN dbo.Permissions p ON p.Id = mp.PermissionId WHERE p.CreatedBy = @m',
        N'@n int OUTPUT, @m uniqueidentifier', @n = @menuRefs OUTPUT, @m = @Marker;
IF @menuRefs > 0
    THROW 51005, 'Rollback refused: a menu uses a permission added by migration 031', 1;
IF OBJECT_ID(N'dbo.WorkTaskActivities', N'U') IS NOT NULL DROP TABLE dbo.WorkTaskActivities;
IF OBJECT_ID(N'dbo.WorkTaskChecklistItems', N'U') IS NOT NULL DROP TABLE dbo.WorkTaskChecklistItems;
IF OBJECT_ID(N'dbo.WorkTaskMembers', N'U') IS NOT NULL DROP TABLE dbo.WorkTaskMembers;
IF OBJECT_ID(N'dbo.WorkTasks', N'U') IS NOT NULL DROP TABLE dbo.WorkTasks;
IF OBJECT_ID(N'dbo.WorkTaskTemplates', N'U') IS NOT NULL DROP TABLE dbo.WorkTaskTemplates;
IF OBJECT_ID(N'dbo.CustomerContractFiles', N'U') IS NOT NULL DROP TABLE dbo.CustomerContractFiles;
IF OBJECT_ID(N'dbo.CustomerContracts', N'U') IS NOT NULL DROP TABLE dbo.CustomerContracts;
DELETE FROM dbo.NumberSequences WHERE Code = 'TASK_CODE' AND CurrentValue = 0;
IF EXISTS (SELECT 1 FROM dbo.NumberSequences WHERE Code = 'TASK_CODE')
    THROW 51006, 'Rollback refused: TASK_CODE sequence has already issued numbers', 1;
DECLARE @grants int, @permissions int;
DELETE FROM dbo.RolePermissions WHERE CreatedBy = @Marker;
SET @grants = @@ROWCOUNT;
DELETE FROM dbo.Permissions WHERE CreatedBy = @Marker;
SET @permissions = @@ROWCOUNT;
COMMIT;
PRINT N'INFO: SYS_ADMIN grants removed = ' + CAST(@grants AS nvarchar(10));
PRINT N'INFO: permissions removed = ' + CAST(@permissions AS nvarchar(10));
PRINT N'052-rollback-contracts-and-work-tasks.sql: OK';
