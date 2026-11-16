-- CONTRACTS-AND-TASKS - rollback of migrations/061-service-catalog.sql: removes Customers.IndustryId, the service
-- catalogue tables (services, groups and industries entered after 061 are lost) and the permission rows the migration
-- inserted (marker A0000061-...). After it, 109-verify-service-catalog.sql must print VERIFY_STATUS=NOT_APPLIED.
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @Marker uniqueidentifier = 'A0000061-0000-4000-8000-000000000061';
IF OBJECT_ID(N'dbo.FK_Customers_Industry', N'F') IS NOT NULL BEGIN ALTER TABLE dbo.Customers DROP CONSTRAINT FK_Customers_Industry; PRINT N'FK_Customers_Industry: dropped'; END
IF COL_LENGTH(N'dbo.Customers', N'IndustryId') IS NOT NULL BEGIN EXEC (N'ALTER TABLE dbo.Customers DROP COLUMN IndustryId;'); PRINT N'dbo.Customers.IndustryId: dropped'; END
IF OBJECT_ID(N'dbo.Services', N'U') IS NOT NULL BEGIN DROP TABLE dbo.Services; PRINT N'dbo.Services: dropped'; END
IF OBJECT_ID(N'dbo.ServiceCategories', N'U') IS NOT NULL BEGIN DROP TABLE dbo.ServiceCategories; PRINT N'dbo.ServiceCategories: dropped'; END
IF OBJECT_ID(N'dbo.Industries', N'U') IS NOT NULL BEGIN DROP TABLE dbo.Industries; PRINT N'dbo.Industries: dropped'; END
DELETE rp FROM dbo.RolePermissions rp JOIN dbo.Permissions p ON p.Id = rp.PermissionId WHERE p.Code = 'CONTRACTS.SERVICES.MANAGE';
PRINT N'INFO: role grants removed = ' + CAST(@@ROWCOUNT AS nvarchar(10));
DELETE FROM dbo.Permissions WHERE Code = 'CONTRACTS.SERVICES.MANAGE' AND CreatedBy = @Marker;
PRINT N'INFO: permission rows removed = ' + CAST(@@ROWCOUNT AS nvarchar(10));
COMMIT TRANSACTION;
PRINT N'110-rollback-service-catalog: done';
