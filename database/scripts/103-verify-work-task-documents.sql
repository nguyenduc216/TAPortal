-- CONTRACTS-AND-TASKS - READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/058-work-task-documents.sql. Expected last line: VERIFY_STATUS=PASS (NOT_APPLIED before the migration).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
IF OBJECT_ID(N'dbo.WorkTaskDocuments', N'U') IS NULL BEGIN PRINT N'INFO: dbo.WorkTaskDocuments missing'; PRINT N'VERIFY_STATUS=NOT_APPLIED'; RETURN; END
DECLARE @ok bit = 1;
IF COL_LENGTH(N'dbo.WorkTaskDocuments', N'Data') = -1 PRINT N'PASS: Data varbinary(max)'; ELSE BEGIN PRINT N'FAIL: Data'; SET @ok = 0; END
IF COL_LENGTH(N'dbo.WorkTaskDocuments', N'Note') IS NOT NULL PRINT N'PASS: Note'; ELSE BEGIN PRINT N'FAIL: Note'; SET @ok = 0; END
IF OBJECT_ID(N'dbo.FK_WorkTaskDocuments_Task', N'F') IS NOT NULL PRINT N'PASS: FK to WorkTasks'; ELSE BEGIN PRINT N'FAIL: FK_WorkTaskDocuments_Task'; SET @ok = 0; END
IF OBJECT_ID(N'dbo.CK_WorkTaskDocuments_Size', N'C') IS NOT NULL PRINT N'PASS: size check'; ELSE BEGIN PRINT N'FAIL: CK_WorkTaskDocuments_Size'; SET @ok = 0; END
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WorkTaskDocuments_Task' AND object_id = OBJECT_ID(N'dbo.WorkTaskDocuments')) PRINT N'PASS: IX_WorkTaskDocuments_Task'; ELSE BEGIN PRINT N'FAIL: IX_WorkTaskDocuments_Task'; SET @ok = 0; END
PRINT N'VERIFY_STATUS=' + CASE WHEN @ok = 1 THEN N'PASS' ELSE N'FAIL' END;
