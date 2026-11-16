-- CONTRACTS-AND-TASKS - rollback of migrations/058-work-task-documents.sql: drops dbo.WorkTaskDocuments
-- (documents uploaded after 058 are lost). After it, 103-verify-work-task-documents.sql must print VERIFY_STATUS=NOT_APPLIED.
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.WorkTaskDocuments', N'U') IS NOT NULL BEGIN DROP TABLE dbo.WorkTaskDocuments; PRINT N'dbo.WorkTaskDocuments: dropped'; END
COMMIT TRANSACTION;
PRINT N'104-rollback-work-task-documents: done';
