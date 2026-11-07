-- CUSTOMER-PORTAL-FOUNDATION — ROLLBACK of migrations/030-drop-customer-member-permissions.sql.
-- Restores what 029 added: the empty column dbo.CustomerPortalMemberships.Permissions and role MEMBER in CK_CPM_Role.
-- The dropped column held no data (030 refuses otherwise), so nothing is lost or needs restoring.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
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
PRINT N'050-rollback-drop-customer-member-permissions.sql: OK';
