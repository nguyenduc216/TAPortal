-- CUSTOMER-PORTAL-FOUNDATION-CHG-20261104T112825Z-F9FA74 — ROLLBACK of migrations/018-service-packages.sql.
-- Drops dbo.ServicePackagePurchases and dbo.ServicePackages. Refuses when a purchase is already PAID (customers hold a
-- balance from it): such data must be exported and decided by the operator first.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.ServicePackagePurchases', N'U') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.ServicePackagePurchases WHERE Status = 'PAID')
        THROW 51002, 'Rollback refused: paid package purchases exist (customer balances); export them first', 1;
    DROP TABLE dbo.ServicePackagePurchases;
END
IF OBJECT_ID(N'dbo.ServicePackages', N'U') IS NOT NULL DROP TABLE dbo.ServicePackages;
COMMIT;
PRINT N'026-rollback-service-packages.sql: OK (tables dropped)';
