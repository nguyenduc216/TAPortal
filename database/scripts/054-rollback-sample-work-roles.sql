-- CONTRACTS-AND-TASKS - ROLLBACK of migrations/032-sample-work-roles.sql.
-- Removes only the roles and grants 032 inserted (CreatedBy = 'A0000032-0000-4000-8000-000000000032'). Refuses while a user
-- holds one of those roles: remove the role from the users on "Nguoi dung" first.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @Marker uniqueidentifier = 'A0000032-0000-4000-8000-000000000032';
IF EXISTS (SELECT 1 FROM dbo.UserRoles ur JOIN dbo.Roles r ON r.Id = ur.RoleId WHERE r.CreatedBy = @Marker)
    THROW 51002, 'Rollback refused: a user holds a role added by migration 032', 1;
DECLARE @grants int, @roles int;
DELETE FROM dbo.RolePermissions WHERE RoleId IN (SELECT Id FROM dbo.Roles WHERE CreatedBy = @Marker);
SET @grants = @@ROWCOUNT;
DELETE FROM dbo.Roles WHERE CreatedBy = @Marker;
SET @roles = @@ROWCOUNT;
COMMIT;
PRINT N'INFO: grants removed = ' + CAST(@grants AS nvarchar(10));
PRINT N'INFO: roles removed = ' + CAST(@roles AS nvarchar(10));
PRINT N'054-rollback-sample-work-roles.sql: OK';
