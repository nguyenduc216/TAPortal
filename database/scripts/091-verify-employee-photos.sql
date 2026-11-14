-- HR-EMPLOYEES - READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/051-employee-photos.sql. Expected last line: VERIFY_STATUS=PASS (NOT_APPLIED before the migration).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
IF OBJECT_ID(N'dbo.EmployeePhotos', N'U') IS NULL BEGIN PRINT N'INFO: dbo.EmployeePhotos missing'; PRINT N'VERIFY_STATUS=NOT_APPLIED'; RETURN; END
DECLARE @fail int = 0;
IF COL_LENGTH(N'dbo.EmployeePhotos', N'Data') IS NULL OR COL_LENGTH(N'dbo.EmployeePhotos', N'ContentType') IS NULL OR COL_LENGTH(N'dbo.EmployeePhotos', N'UpdatedAt') IS NULL
    BEGIN PRINT N'FAIL: EmployeePhotos columns missing'; SET @fail += 1; END
IF OBJECT_ID(N'dbo.FK_EmployeePhotos_Employees', N'F') IS NULL
    BEGIN PRINT N'FAIL: FK_EmployeePhotos_Employees missing'; SET @fail += 1; END
IF OBJECT_ID(N'dbo.CK_EmployeePhotos_Size', N'C') IS NULL OR OBJECT_ID(N'dbo.CK_EmployeePhotos_Type', N'C') IS NULL
    BEGIN PRINT N'FAIL: size/type checks missing'; SET @fail += 1; END
IF @fail = 0 PRINT N'PASS: employee photo table, key and checks';
PRINT N'VERIFY_STATUS=' + CASE WHEN @fail = 0 THEN N'PASS' ELSE N'FAIL' END;
