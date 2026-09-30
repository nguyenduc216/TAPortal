/* TAPortal 023 - Customer invoice identity/profile and invoice validation snapshot. */
USE [TAPortal];
GO
IF OBJECT_ID(N'dbo.CustomerInvoiceProfiles',N'U') IS NULL BEGIN
 CREATE TABLE dbo.CustomerInvoiceProfiles(Id uniqueidentifier NOT NULL DEFAULT NEWSEQUENTIALID() CONSTRAINT PK_CustomerInvoiceProfiles PRIMARY KEY,PartnerId uniqueidentifier NOT NULL,CustomerId uniqueidentifier NOT NULL,ProfileName nvarchar(200) NOT NULL DEFAULT N'Mặc định',BuyerName nvarchar(300) NULL,CompanyName nvarchar(300) NULL,TaxCode nvarchar(50) NULL,Address nvarchar(500) NULL,Email nvarchar(250) NULL,Phone nvarchar(50) NULL,BankAccount nvarchar(100) NULL,BankName nvarchar(200) NULL,IsDefault bit NOT NULL DEFAULT 0,IsActive bit NOT NULL DEFAULT 1,CreatedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),UpdatedAt datetime2(3) NULL,RowVersion rowversion NOT NULL,CONSTRAINT FK_CIPF_Partner FOREIGN KEY(PartnerId) REFERENCES dbo.Partners(Id),CONSTRAINT FK_CIPF_Customer FOREIGN KEY(CustomerId) REFERENCES dbo.Customers(Id));
 CREATE UNIQUE INDEX UX_CIPF_Default ON dbo.CustomerInvoiceProfiles(CustomerId) WHERE IsDefault=1 AND IsActive=1;
END
GO
IF COL_LENGTH('dbo.InvoiceRequests','CustomerInvoiceProfileId') IS NULL ALTER TABLE dbo.InvoiceRequests ADD CustomerInvoiceProfileId uniqueidentifier NULL;
IF COL_LENGTH('dbo.InvoiceRequests','InvoiceProfileSnapshot') IS NULL ALTER TABLE dbo.InvoiceRequests ADD InvoiceProfileSnapshot nvarchar(max) NULL;
IF COL_LENGTH('dbo.InvoiceRequests','CurrencyCode') IS NULL ALTER TABLE dbo.InvoiceRequests ADD CurrencyCode char(3) NOT NULL CONSTRAINT DF_IR_Currency DEFAULT 'VND';
IF COL_LENGTH('dbo.InvoiceRequests','ValidationStatus') IS NULL ALTER TABLE dbo.InvoiceRequests ADD ValidationStatus varchar(20) NOT NULL CONSTRAINT DF_IR_Validation DEFAULT 'PENDING';
IF COL_LENGTH('dbo.InvoiceRequests','ValidationMessage') IS NULL ALTER TABLE dbo.InvoiceRequests ADD ValidationMessage nvarchar(2000) NULL;
GO
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name='FK_IR_CustomerInvoiceProfile') ALTER TABLE dbo.InvoiceRequests ADD CONSTRAINT FK_IR_CustomerInvoiceProfile FOREIGN KEY(CustomerInvoiceProfileId) REFERENCES dbo.CustomerInvoiceProfiles(Id);
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name='CK_IR_ValidationStatus') ALTER TABLE dbo.InvoiceRequests ADD CONSTRAINT CK_IR_ValidationStatus CHECK(ValidationStatus IN('PENDING','VALID','INVALID','NEEDS_REVIEW'));
GO
IF OBJECT_ID(N'dbo.InvoiceRequestEvents',N'U') IS NULL BEGIN
 CREATE TABLE dbo.InvoiceRequestEvents(Id bigint IDENTITY(1,1) CONSTRAINT PK_InvoiceRequestEvents PRIMARY KEY,InvoiceRequestId uniqueidentifier NOT NULL,EventType varchar(50) NOT NULL,OldStatus varchar(30) NULL,NewStatus varchar(30) NULL,Message nvarchar(1000) NULL,ActorType varchar(20) NOT NULL DEFAULT 'SYSTEM',ActorId uniqueidentifier NULL,CreatedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),CONSTRAINT FK_IRE_Request FOREIGN KEY(InvoiceRequestId) REFERENCES dbo.InvoiceRequests(Id),CONSTRAINT CK_IRE_Actor CHECK(ActorType IN('SYSTEM','USER','PROVIDER','WORKER')));
 CREATE INDEX IX_IRE_RequestCreated ON dbo.InvoiceRequestEvents(InvoiceRequestId,CreatedAt DESC);
END
GO
PRINT '023-customer-invoice-profile.sql: OK';
GO