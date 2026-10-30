-- TAPORTAL-SYSTEM-AUDIT-CHG-20261028T173600Z-62724B — rollback of OPTIONAL migration 009. NOT_EXECUTED.
-- Manual execution only, after operator approval. Drops only UX_Customers_TaxCode; no data is changed.
-- The application keeps rejecting duplicates at application level after rollback.
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
-- Equivalent required positive guard: DB_NAME() = 'TAPortalSys'. No USE statement.
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Customers') AND name = 'UX_Customers_TaxCode')
 DROP INDEX UX_Customers_TaxCode ON dbo.Customers;
PRINT N'010-rollback-customer-taxcode-unique-index.sql: OK';
