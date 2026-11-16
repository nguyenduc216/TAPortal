-- CONTRACTS-AND-TASKS - READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/041-customer-staff.sql. Expected last line: VERIFY_STATUS=PASS (NOT_APPLIED before the migration).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
IF OBJECT_ID(N'dbo.CustomerStaff', N'U') IS NULL BEGIN PRINT N'INFO: dbo.CustomerStaff missing'; PRINT N'VERIFY_STATUS=NOT_APPLIED'; RETURN; END
DECLARE @fail int = 0;
IF COL_LENGTH(N'dbo.CustomerStaff', N'Role') IS NULL BEGIN PRINT N'FAIL: Role column missing'; SET @fail += 1; END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_CustomerStaff_Manager' AND object_id = OBJECT_ID(N'dbo.CustomerStaff') AND is_unique = 1 AND has_filter = 1)
    BEGIN PRINT N'FAIL: UX_CustomerStaff_Manager missing'; SET @fail += 1; END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CustomerStaff_Employee' AND object_id = OBJECT_ID(N'dbo.CustomerStaff'))
    BEGIN PRINT N'FAIL: IX_CustomerStaff_Employee missing'; SET @fail += 1; END
IF OBJECT_ID(N'dbo.CK_CustomerStaff_Role', N'C') IS NULL BEGIN PRINT N'FAIL: CK_CustomerStaff_Role missing'; SET @fail += 1; END
IF @fail = 0 PRINT N'PASS: dbo.CustomerStaff with role check, one-manager index and employee index';
PRINT N'VERIFY_STATUS=' + CASE WHEN @fail = 0 THEN N'PASS' ELSE N'FAIL' END;
