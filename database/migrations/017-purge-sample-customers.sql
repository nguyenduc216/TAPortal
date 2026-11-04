-- CUSTOMER-PORTAL-FOUNDATION-CHG-20261104T103400Z-124DDE — remove every customer except TVGPTA. MIGRATION_NOT_EXECUTED.
-- Operator request 2026-11-04: "xoá hết dữ liệu khách hàng trừ TVGPTA trên portal" (hard delete with a saved copy).
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, after a fresh
-- COPY_ONLY full backup (this migration deletes existing rows).
-- 1. Copies the customers to delete and their contacts into dbo.Backup20261104_Customers / dbo.Backup20261104_CustomerContacts.
-- 2. Deletes those contacts, then those customers. Audit logs are kept.
-- Aborts without changes when TVGPTA is missing, when the backup tables already exist, or when any other table still
-- references a customer being deleted (bank accounts, orders, buyers, memberships, Tingee merchants, ...).
-- Verify: database/scripts/023-verify-purge-sample-customers.sql. Rollback: database/scripts/024-rollback-purge-sample-customers.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
-- Equivalent required positive guard: DB_NAME() = 'TAPortalSys'. No USE statement.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM dbo.Customers WHERE Code = 'TVGPTA')
 THROW 51001, 'Precondition failed: customer TVGPTA missing', 1;
IF OBJECT_ID(N'dbo.Backup20261104_Customers', N'U') IS NOT NULL OR OBJECT_ID(N'dbo.Backup20261104_CustomerContacts', N'U') IS NOT NULL
 THROW 51002, 'Precondition failed: backup tables already exist (already applied?)', 1;

-- Every foreign key into dbo.Customers other than CustomerContacts must have no rows for the customers being deleted.
DECLARE @sql nvarchar(max) = N'', @blocked int = 0;
SELECT @sql = @sql + N'IF EXISTS (SELECT 1 FROM ' + QUOTENAME(OBJECT_SCHEMA_NAME(f.parent_object_id)) + N'.' + QUOTENAME(OBJECT_NAME(f.parent_object_id))
    + N' x JOIN dbo.Customers k ON k.Id = x.' + QUOTENAME(c.name) + N' WHERE k.Code <> ''TVGPTA'') BEGIN PRINT N''BLOCKED: '
    + OBJECT_NAME(f.parent_object_id) + N'.' + c.name + N' has rows''; SET @b += 1; END;' + NCHAR(10)
FROM sys.foreign_keys f
JOIN sys.foreign_key_columns k ON k.constraint_object_id = f.object_id
JOIN sys.columns c ON c.object_id = k.parent_object_id AND c.column_id = k.parent_column_id
WHERE f.referenced_object_id = OBJECT_ID(N'dbo.Customers') AND f.parent_object_id <> OBJECT_ID(N'dbo.CustomerContacts');
EXEC sp_executesql @sql, N'@b int OUTPUT', @b = @blocked OUTPUT;
IF @blocked > 0
 THROW 51003, 'Precondition failed: other tables still reference customers being deleted; nothing deleted', 1;

SELECT Id, Code, Name, TaxCode, Email, Phone, Address, CustomerType, Status, IsActive, IsDeleted, CreatedAt, CreatedBy,
       UpdatedAt, UpdatedBy, DeletedAt, DeletedBy, OwnerBranchId, LogoUrl, SYSUTCDATETIME() AS PurgedAt
INTO dbo.Backup20261104_Customers
FROM dbo.Customers WHERE Code <> 'TVGPTA';

SELECT x.Id, x.CustomerId, x.Name, x.Role, x.Email, x.Phone, x.SortOrder, x.IsDeleted
INTO dbo.Backup20261104_CustomerContacts
FROM dbo.CustomerContacts x JOIN dbo.Backup20261104_Customers b ON b.Id = x.CustomerId;

DELETE x FROM dbo.CustomerContacts x JOIN dbo.Backup20261104_Customers b ON b.Id = x.CustomerId;
PRINT N'CustomerContacts deleted: ' + CAST(@@ROWCOUNT AS nvarchar(20));
DELETE c FROM dbo.Customers c JOIN dbo.Backup20261104_Customers b ON b.Id = c.Id;
PRINT N'Customers deleted: ' + CAST(@@ROWCOUNT AS nvarchar(20));
COMMIT;
PRINT N'017-purge-sample-customers.sql: OK';
