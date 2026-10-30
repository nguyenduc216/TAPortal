-- TAPORTAL-SYSTEM-AUDIT-CHG-20261028T173600Z-62724B — AUD-16 (OPTIONAL, operator review). MIGRATION_NOT_EXECUTED.
-- Manual execution only on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys after backup and after
-- database/scripts/008-audit-customer-taxcode-duplicates.sql reports PASS.
-- Adds database-level uniqueness of TaxCode among non-deleted customers (filtered unique index, SQL Server 2008+).
-- The Admin application already rejects duplicates inside a SERIALIZABLE transaction; this index also covers
-- writers outside the application and is the only guarantee at database level.
-- Rollback: database/scripts/010-rollback-customer-taxcode-unique-index.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
-- Equivalent required positive guard: DB_NAME() = 'TAPortalSys'. No USE statement.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.Customers','U') IS NULL OR COL_LENGTH('dbo.Customers','TaxCode') IS NULL OR COL_LENGTH('dbo.Customers','IsDeleted') IS NULL
 THROW 51000, 'Audit required: dbo.Customers/TaxCode/IsDeleted missing', 1;
IF EXISTS (SELECT TaxCode FROM dbo.Customers WITH (TABLOCKX, HOLDLOCK) WHERE IsDeleted = 0 AND TaxCode IS NOT NULL GROUP BY TaxCode HAVING COUNT(1) > 1)
 THROW 51000, 'Duplicate TaxCode values among non-deleted customers; run scripts/008 and resolve first. No changes executed', 1;
IF EXISTS (SELECT 1 FROM dbo.Customers WHERE IsDeleted = 0 AND TaxCode IS NOT NULL AND TaxCode <> REPLACE(REPLACE(REPLACE(TaxCode,' ',''),CHAR(9),''),CHAR(160),''))
 THROW 51000, 'TaxCode values with whitespace exist; normalize them by approved data change first. No changes executed', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Customers') AND name = 'UX_Customers_TaxCode')
 CREATE UNIQUE NONCLUSTERED INDEX UX_Customers_TaxCode ON dbo.Customers(TaxCode) WHERE TaxCode IS NOT NULL AND IsDeleted = 0;
COMMIT;
PRINT N'009-customer-taxcode-unique-index.sql: OK';
