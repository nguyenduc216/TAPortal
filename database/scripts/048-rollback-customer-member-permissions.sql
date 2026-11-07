-- CUSTOMER-PORTAL-FOUNDATION — ROLLBACK of migrations/029-customer-member-permissions.sql.
-- Restores CK_CPM_Role to OWNER, ADMIN, VIEWER and drops dbo.CustomerPortalMemberships.Permissions. Refuses while a
-- MEMBER membership or a stored permission exists (staff accounts created by companies): decide with the operator first.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF EXISTS (SELECT 1 FROM dbo.CustomerPortalMemberships WHERE PortalRole = 'MEMBER')
    THROW 51002, 'Rollback refused: memberships with PortalRole MEMBER exist', 1;
IF COL_LENGTH(N'dbo.CustomerPortalMemberships', N'Permissions') IS NOT NULL
BEGIN
    DECLARE @granted int;
    EXEC sp_executesql N'SELECT @g = COUNT(*) FROM dbo.CustomerPortalMemberships WHERE Permissions IS NOT NULL', N'@g int OUTPUT', @g = @granted OUTPUT;
    IF @granted > 0
        THROW 51003, 'Rollback refused: memberships with stored Permissions exist', 1;
END
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
PRINT N'048-rollback-customer-member-permissions.sql: OK';
