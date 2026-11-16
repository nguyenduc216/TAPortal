-- HR-EMPLOYEES - rollback of migrations/051-employee-photos.sql: drops dbo.EmployeePhotos (photos uploaded after 051
-- are lost; avatars fall back to initials). After it, 091-verify-employee-photos.sql must print VERIFY_STATUS=NOT_APPLIED.
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.EmployeePhotos', N'U') IS NOT NULL BEGIN DROP TABLE dbo.EmployeePhotos; PRINT N'dbo.EmployeePhotos: dropped'; END
COMMIT TRANSACTION;
PRINT N'092-rollback-employee-photos: done';
