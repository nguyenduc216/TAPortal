/* Run manually on the portal database before deploying the customer UI. */
IF COL_LENGTH('dbo.Customers','LogoUrl') IS NULL
    ALTER TABLE dbo.Customers ADD LogoUrl nvarchar(500) NULL;
GO
IF OBJECT_ID('dbo.CustomerContacts','U') IS NULL
BEGIN
    CREATE TABLE dbo.CustomerContacts (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_CustomerContacts PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        CustomerId uniqueidentifier NOT NULL CONSTRAINT FK_CustomerContacts_Customers REFERENCES dbo.Customers(Id),
        Name nvarchar(150) NOT NULL,
        Role nvarchar(100) NULL,
        Email nvarchar(256) NULL,
        Phone nvarchar(50) NULL,
        SortOrder int NOT NULL DEFAULT 0,
        IsDeleted bit NOT NULL DEFAULT 0
    );
    CREATE INDEX IX_CustomerContacts_CustomerId ON dbo.CustomerContacts(CustomerId);
END;
GO