-- CUSTOMER-PORTAL-FOUNDATION - READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/036-portal-content.sql. Expected last line: VERIFY_STATUS=PASS (NOT_APPLIED before the migration).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
DECLARE @fail int = 0;
DECLARE @Marker uniqueidentifier = 'A0000036-0000-4000-8000-000000000036';
DECLARE @Catalogue TABLE (Code varchar(200) COLLATE DATABASE_DEFAULT NOT NULL PRIMARY KEY);
INSERT @Catalogue (Code) VALUES ('INVOICEAPP.CONTENT.VIEW'), ('INVOICEAPP.CONTENT.EDIT');
DECLARE @total int = (SELECT COUNT(1) FROM @Catalogue);
DECLARE @present int = (SELECT COUNT(DISTINCT c.Code) FROM @Catalogue c JOIN dbo.Permissions p ON p.Code = c.Code AND p.IsDeleted = 0);
PRINT N'=== PORTAL CONTENT VERIFY ===';
IF OBJECT_ID(N'dbo.PortalContent', N'U') IS NULL AND @present = 0
BEGIN
    PRINT N'INFO: dbo.PortalContent and INVOICEAPP.* permissions missing';
    PRINT N'VERIFY_STATUS=NOT_APPLIED';
    RETURN;
END
IF OBJECT_ID(N'dbo.PortalContent', N'U') IS NOT NULL PRINT N'PASS: dbo.PortalContent'; ELSE BEGIN PRINT N'FAIL: dbo.PortalContent missing'; SET @fail += 1; END
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_PCt_Code' AND is_disabled = 0)
    PRINT N'PASS: CK_PCt_Code'; ELSE BEGIN PRINT N'FAIL: CK_PCt_Code missing'; SET @fail += 1; END
IF @present = @total PRINT N'PASS: 2 INVOICEAPP.* permissions'; ELSE BEGIN PRINT N'FAIL: INVOICEAPP.* permissions missing'; SET @fail += 1; END
IF (SELECT COUNT(DISTINCT c.Code) FROM @Catalogue c JOIN dbo.Permissions p ON p.Code = c.Code AND p.IsDeleted = 0
    JOIN dbo.RolePermissions rp ON rp.PermissionId = p.Id JOIN dbo.Roles r ON r.Id = rp.RoleId AND r.Code = 'SYS_ADMIN') = @total
    PRINT N'PASS: SYS_ADMIN has INVOICEAPP.*'; ELSE BEGIN PRINT N'FAIL: SYS_ADMIN grant missing'; SET @fail += 1; END
IF EXISTS (SELECT 1 FROM dbo.Permissions WHERE CreatedBy = @Marker) PRINT N'INFO: rows with marker present';
IF @fail = 0 PRINT N'VERIFY_STATUS=PASS'; ELSE PRINT N'VERIFY_STATUS=FAIL';
