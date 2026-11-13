-- ADMIN-ROLE-PERMISSIONS - rollback of migrations/037-remove-test-accounts-and-tasks.sql.
-- Restores exactly the rows the migration soft-deleted (DeletedAt = its marker 2026-11-13 00:37:00).
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @marker datetime2(3) = '2026-11-13T00:37:00';
UPDATE dbo.Users SET IsDeleted = 0, DeletedAt = NULL, DeletedBy = NULL WHERE IsDeleted = 1 AND DeletedAt = @marker;
UPDATE dbo.Employees SET IsDeleted = 0, DeletedAt = NULL, DeletedBy = NULL WHERE IsDeleted = 1 AND DeletedAt = @marker;
UPDATE dbo.WorkTasks SET IsDeleted = 0, DeletedAt = NULL, DeletedBy = NULL WHERE IsDeleted = 1 AND DeletedAt = @marker;
UPDATE dbo.WorkTaskChecklistItems SET IsDeleted = 0, DeletedAt = NULL, DeletedBy = NULL WHERE IsDeleted = 1 AND DeletedAt = @marker;
COMMIT;
PRINT N'064-rollback-remove-test-accounts-and-tasks.sql: OK';
