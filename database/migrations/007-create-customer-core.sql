/* TAPortal - CMS core 007
   Customer core for the first Tabler portal UI.
   SQL Server 2014+ compatible; dbo only.
*/
USE [TAPortal];
GO

IF OBJECT_ID(N'dbo.Customers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Customers (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_Customers PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        Code varchar(50) NOT NULL,
        Name nvarchar(250) NOT NULL,
        TaxCode varchar(50) NULL,
        Email nvarchar(256) NULL,
        Phone nvarchar(50) NULL,
        Address nvarchar(1000) NULL,
        CustomerType varchar(30) NOT NULL CONSTRAINT DF_Customers_Type DEFAULT 'BUSINESS',
        Status varchar(30) NOT NULL CONSTRAINT DF_Customers_Status DEFAULT 'ACTIVE',
        IsActive bit NOT NULL CONSTRAINT DF_Customers_IsActive DEFAULT 1,
        IsDeleted bit NOT NULL CONSTRAINT DF_Customers_IsDeleted DEFAULT 0,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_Customers_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        UpdatedAt datetime2(3) NULL,
        UpdatedBy uniqueidentifier NULL,
        DeletedAt datetime2(3) NULL,
        DeletedBy uniqueidentifier NULL,
        RowVersion rowversion NOT NULL
    );
    CREATE UNIQUE INDEX UX_Customers_Code ON dbo.Customers(Code) WHERE IsDeleted=0;
    CREATE INDEX IX_Customers_TaxCode ON dbo.Customers(TaxCode) WHERE TaxCode IS NOT NULL AND IsDeleted=0;
END
GO

PRINT '007-create-customer-core.sql: OK';
GO
