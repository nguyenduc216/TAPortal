-- Stop customer site and disable admin account-management UI first. Backup before rollback.
-- Preserve all populated membership data. No destructive rollback of existing data.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.CustomerPortalMemberships','U') IS NOT NULL
BEGIN
 IF EXISTS(SELECT 1 FROM dbo.CustomerPortalMemberships) THROW 51000,'Export/archive memberships before approved manual removal; rollback refused',1;
 DROP TABLE dbo.CustomerPortalMemberships;
END;
-- Permission rows are deliberately retained to preserve grants/audit history.
COMMIT;
