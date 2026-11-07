-- CUSTOMER-PORTAL-FOUNDATION — company staff accounts and per-function rights (operator request 2026-11-07). MIGRATION_NOT_EXECUTED.
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, in numeric order (after 027 and 028).
-- Independent of 009-028 except 008 (dbo.CustomerPortalMemberships) and 015 (dbo.Users.MustChangePassword, used by the app).
-- The Customer Portal page "Tài khoản & phân quyền" lets a company create its own staff accounts (role MEMBER) and
-- choose, per function, Xem (V) or Quản lý (M):
--   dbo.CustomerPortalMemberships  + Permissions varchar(500) NULL   e.g. 'ORDERS:M;CONTACTS:V' (only read for MEMBER)
--   dbo.CustomerPortalMemberships  CK_CPM_Role  OWNER, ADMIN, VIEWER  ->  + MEMBER
-- Adds a nullable column and widens a CHECK constraint; no existing row is changed. Idempotent.
-- Verify: database/scripts/047-verify-customer-member-permissions.sql. Rollback: database/scripts/048-rollback-customer-member-permissions.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
-- Equivalent required positive guard: DB_NAME() = 'TAPortalSys'. No USE statement.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.CustomerPortalMemberships', N'U') IS NULL OR COL_LENGTH(N'dbo.CustomerPortalMemberships', N'PortalRole') IS NULL
 THROW 51001, 'Precondition failed: dbo.CustomerPortalMemberships.PortalRole missing (run 008 first)', 1;
IF COL_LENGTH(N'dbo.CustomerPortalMemberships', N'Permissions') IS NULL
    ALTER TABLE dbo.CustomerPortalMemberships ADD Permissions varchar(500) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_CPM_Role' AND parent_object_id = OBJECT_ID(N'dbo.CustomerPortalMemberships')
               AND definition LIKE N'%MEMBER%')
BEGIN
    IF OBJECT_ID(N'dbo.CK_CPM_Role', N'C') IS NOT NULL
        ALTER TABLE dbo.CustomerPortalMemberships DROP CONSTRAINT CK_CPM_Role;
    ALTER TABLE dbo.CustomerPortalMemberships WITH CHECK ADD CONSTRAINT CK_CPM_Role
        CHECK (PortalRole IN ('OWNER','ADMIN','VIEWER','MEMBER'));
END
COMMIT;
PRINT N'029-customer-member-permissions.sql: OK';
