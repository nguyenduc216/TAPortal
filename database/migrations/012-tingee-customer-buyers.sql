-- TINGEE-INTEGRATION-CHG-20261030T131324Z-F23ADA — Separate table for each company's own customers. MIGRATION_NOT_EXECUTED.
-- Manual execution only (SSMS/DBeaver) on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, after a backup.
-- Why: dbo.CustomerContacts existed before migration 011 with other columns and data, so 011 skipped creating it and pointed
-- FK_CO_Contact at it. This creates dbo.CustomerBuyers and moves the order foreign key there.
-- dbo.CustomerContacts and its rows are never changed; only the foreign key added by 011 is removed.
-- Precondition: run after 011-tingee-payments-foundation.sql; aborts if any order already has a ContactId. Idempotent.
-- Verify: database/scripts/012-verify-tingee-foundation.sql and 014-diagnose-tingee-foundation.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
-- Equivalent required positive guard: DB_NAME() = 'TAPortalSys'. No USE statement.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.Customers', N'U') IS NULL OR OBJECT_ID(N'dbo.CustomerOrders', N'U') IS NULL
 THROW 51001, 'Precondition failed: dbo.Customers / dbo.CustomerOrders missing (run 011 first)', 1;
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CO_Contact' AND parent_object_id = OBJECT_ID(N'dbo.CustomerOrders'))
   AND EXISTS (SELECT 1 FROM dbo.CustomerOrders WHERE ContactId IS NOT NULL)
 THROW 51002, 'Precondition failed: orders already reference dbo.CustomerContacts; no changes executed', 1;

IF OBJECT_ID(N'dbo.CustomerBuyers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustomerBuyers (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_CustomerBuyers PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        CustomerId uniqueidentifier NOT NULL,
        Code varchar(50) NOT NULL,
        Name nvarchar(250) NOT NULL,
        LegalName nvarchar(250) NULL,
        TaxCode varchar(20) NULL,
        Phone varchar(20) NULL,
        Email nvarchar(256) NULL,
        Address nvarchar(500) NULL,
        Note nvarchar(1000) NULL,
        IsActive bit NOT NULL CONSTRAINT DF_CB_Active DEFAULT 1,
        IsDeleted bit NOT NULL CONSTRAINT DF_CB_Deleted DEFAULT 0,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_CB_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        UpdatedAt datetime2(3) NULL,
        UpdatedBy uniqueidentifier NULL,
        CONSTRAINT FK_CB_Customer FOREIGN KEY (CustomerId) REFERENCES dbo.Customers(Id)
    );
    CREATE UNIQUE INDEX UX_CB_Customer_Code ON dbo.CustomerBuyers(CustomerId, Code) WHERE IsDeleted = 0;
    CREATE INDEX IX_CB_Customer_Name ON dbo.CustomerBuyers(CustomerId, IsDeleted, Name);
    PRINT N'dbo.CustomerBuyers created';
END ELSE PRINT N'dbo.CustomerBuyers exists';

IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CO_Contact' AND parent_object_id = OBJECT_ID(N'dbo.CustomerOrders'))
BEGIN
    ALTER TABLE dbo.CustomerOrders DROP CONSTRAINT FK_CO_Contact;
    PRINT N'FK_CO_Contact removed';
END
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CO_Buyer' AND parent_object_id = OBJECT_ID(N'dbo.CustomerOrders'))
BEGIN
    EXEC(N'ALTER TABLE dbo.CustomerOrders WITH CHECK ADD CONSTRAINT FK_CO_Buyer FOREIGN KEY (ContactId) REFERENCES dbo.CustomerBuyers(Id);');
    PRINT N'FK_CO_Buyer created';
END ELSE PRINT N'FK_CO_Buyer exists';
COMMIT;
PRINT N'012-tingee-customer-buyers.sql: OK';
