-- TINGEE-INTEGRATION - ROLLBACK of migrations/069-payment-collections.sql.
-- Drops PaymentTransactions.CollectionId and the three payment collection tables. Refuses once a collection exists (its
-- payments and their split over orders would be lost): decide with the operator first.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.PaymentCollections', N'U') IS NOT NULL
    EXEC sys.sp_executesql N'IF EXISTS (SELECT 1 FROM dbo.PaymentCollections) THROW 51002, ''Rollback refused: payment collections exist'', 1;';
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.PaymentTransactions') AND name = N'IX_PT_Collection')
    DROP INDEX IX_PT_Collection ON dbo.PaymentTransactions;
IF OBJECT_ID(N'dbo.FK_PT_Collection', N'F') IS NOT NULL
    ALTER TABLE dbo.PaymentTransactions DROP CONSTRAINT FK_PT_Collection;
IF COL_LENGTH(N'dbo.PaymentTransactions', N'CollectionId') IS NOT NULL
    ALTER TABLE dbo.PaymentTransactions DROP COLUMN CollectionId;
IF OBJECT_ID(N'dbo.PaymentAllocations', N'U') IS NOT NULL DROP TABLE dbo.PaymentAllocations;
IF OBJECT_ID(N'dbo.PaymentCollectionOrders', N'U') IS NOT NULL DROP TABLE dbo.PaymentCollectionOrders;
IF OBJECT_ID(N'dbo.PaymentCollections', N'U') IS NOT NULL DROP TABLE dbo.PaymentCollections;
COMMIT;
PRINT N'126-rollback-payment-collections.sql: OK';
