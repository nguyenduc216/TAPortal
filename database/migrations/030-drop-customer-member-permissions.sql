-- CUSTOMER-PORTAL-FOUNDATION — remove the unused Customer Portal staff permissions (operator decision 2026-11-07). MIGRATION_NOT_EXECUTED.
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, after 029.
-- Account and permission management is done on the TA Portal only; the Customer Portal feature of 029 was reverted
-- (commit 03432e8) and the operator asked to delete its database part ("Xóa dữ liệu"):
--   dbo.CustomerPortalMemberships  - Permissions varchar(500) NULL
--   dbo.CustomerPortalMemberships  CK_CPM_Role  OWNER, ADMIN, VIEWER, MEMBER  ->  OWNER, ADMIN, VIEWER
-- Refuses (no change) while a membership is MEMBER or has Permissions: nothing in use is dropped. Idempotent.
-- Verify: database/scripts/049-verify-drop-customer-member-permissions.sql. Rollback: database/scripts/050-rollback-drop-customer-member-permissions.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
-- Equivalent required positive guard: DB_NAME() = 'TAPortalSys'. No USE statement.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.CustomerPortalMemberships', N'U') IS NULL
 THROW 51001, 'Precondition failed: dbo.CustomerPortalMemberships missing', 1;
DECLARE @used int = 0;
IF COL_LENGTH(N'dbo.CustomerPortalMemberships', N'Permissions') IS NOT NULL
    EXEC sys.sp_executesql N'SELECT @n = COUNT(*) FROM dbo.CustomerPortalMemberships WHERE PortalRole = ''MEMBER'' OR Permissions IS NOT NULL', N'@n int OUTPUT', @n = @used OUTPUT;
ELSE
    SELECT @used = COUNT(*) FROM dbo.CustomerPortalMemberships WHERE PortalRole = 'MEMBER';
IF @used > 0
 THROW 51002, 'Refused: memberships with role MEMBER or Permissions exist', 1;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_CPM_Role' AND parent_object_id = OBJECT_ID(N'dbo.CustomerPortalMemberships')
           AND definition LIKE N'%MEMBER%')
BEGIN
    ALTER TABLE dbo.CustomerPortalMemberships DROP CONSTRAINT CK_CPM_Role;
    ALTER TABLE dbo.CustomerPortalMemberships WITH CHECK ADD CONSTRAINT CK_CPM_Role
        CHECK (PortalRole IN ('OWNER','ADMIN','VIEWER'));
END
IF COL_LENGTH(N'dbo.CustomerPortalMemberships', N'Permissions') IS NOT NULL
    ALTER TABLE dbo.CustomerPortalMemberships DROP COLUMN Permissions;
COMMIT;
PRINT N'030-drop-customer-member-permissions.sql: OK';
