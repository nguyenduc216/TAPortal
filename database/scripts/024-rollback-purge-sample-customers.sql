-- CUSTOMER-PORTAL-FOUNDATION-CHG-20261104T103400Z-124DDE — ROLLBACK of migrations/017-purge-sample-customers.sql.
-- Re-inserts the saved customers and their contacts (same Ids) from dbo.Backup20261104_*, then drops the backup tables.
-- Customers.RowVersion gets a new value (timestamp). Rows whose Id or Code already exist again are skipped (reported).
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.Backup20261104_Customers', N'U') IS NULL
BEGIN
    PRINT N'024-rollback-purge-sample-customers.sql: nothing to restore (no backup table)';
    COMMIT;
    RETURN;
END
INSERT dbo.Customers (Id, Code, Name, TaxCode, Email, Phone, Address, CustomerType, Status, IsActive, IsDeleted, CreatedAt, CreatedBy,
                      UpdatedAt, UpdatedBy, DeletedAt, DeletedBy, OwnerBranchId, LogoUrl)
SELECT b.Id, b.Code, b.Name, b.TaxCode, b.Email, b.Phone, b.Address, b.CustomerType, b.Status, b.IsActive, b.IsDeleted, b.CreatedAt, b.CreatedBy,
       b.UpdatedAt, b.UpdatedBy, b.DeletedAt, b.DeletedBy, b.OwnerBranchId, b.LogoUrl
FROM dbo.Backup20261104_Customers b
WHERE NOT EXISTS (SELECT 1 FROM dbo.Customers c WHERE c.Id = b.Id OR c.Code = b.Code);
PRINT N'Customers restored: ' + CAST(@@ROWCOUNT AS nvarchar(20));
IF OBJECT_ID(N'dbo.Backup20261104_CustomerContacts', N'U') IS NOT NULL
BEGIN
    INSERT dbo.CustomerContacts (Id, CustomerId, Name, Role, Email, Phone, SortOrder, IsDeleted)
    SELECT b.Id, b.CustomerId, b.Name, b.Role, b.Email, b.Phone, b.SortOrder, b.IsDeleted
    FROM dbo.Backup20261104_CustomerContacts b
    WHERE EXISTS (SELECT 1 FROM dbo.Customers c WHERE c.Id = b.CustomerId)
      AND NOT EXISTS (SELECT 1 FROM dbo.CustomerContacts x WHERE x.Id = b.Id);
    PRINT N'CustomerContacts restored: ' + CAST(@@ROWCOUNT AS nvarchar(20));
    DROP TABLE dbo.Backup20261104_CustomerContacts;
END
DROP TABLE dbo.Backup20261104_Customers;
COMMIT;
PRINT N'024-rollback-purge-sample-customers.sql: OK (backup tables dropped)';
