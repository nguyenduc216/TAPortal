/* TAPortal 009 - Partner/Tenant foundation. Idempotent. */
USE [TAPortal];
GO
IF OBJECT_ID(N'dbo.Partners',N'U') IS NULL BEGIN
 CREATE TABLE dbo.Partners(Id uniqueidentifier NOT NULL CONSTRAINT PK_Partners PRIMARY KEY DEFAULT NEWSEQUENTIALID(),Code varchar(50) NOT NULL,PartnerType varchar(30) NOT NULL,Name nvarchar(250) NOT NULL,LegalName nvarchar(300) NULL,TaxCode varchar(50) NULL,RepresentativeName nvarchar(200) NULL,Email nvarchar(256) NULL,Phone nvarchar(50) NULL,Address nvarchar(1000) NULL,Status varchar(30) NOT NULL CONSTRAINT DF_Partners_Status DEFAULT 'ACTIVE',IsPlatformOwner bit NOT NULL CONSTRAINT DF_Partners_Platform DEFAULT 0,IsActive bit NOT NULL CONSTRAINT DF_Partners_Active DEFAULT 1,IsDeleted bit NOT NULL CONSTRAINT DF_Partners_Deleted DEFAULT 0,CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_Partners_Created DEFAULT SYSUTCDATETIME(),UpdatedAt datetime2(3) NULL,RowVersion rowversion NOT NULL,CONSTRAINT CK_Partners_Type CHECK(PartnerType IN('ENTERPRISE','HOUSEHOLD_BUSINESS','INDIVIDUAL')),CONSTRAINT CK_Partners_Status CHECK(Status IN('PENDING','ACTIVE','SUSPENDED','TERMINATED')));
 CREATE UNIQUE INDEX UX_Partners_Code ON dbo.Partners(Code) WHERE IsDeleted=0; CREATE INDEX IX_Partners_TaxCode ON dbo.Partners(TaxCode) WHERE TaxCode IS NOT NULL AND IsDeleted=0;
END
GO
IF OBJECT_ID(N'dbo.PartnerUsers',N'U') IS NULL BEGIN
 CREATE TABLE dbo.PartnerUsers(PartnerId uniqueidentifier NOT NULL,UserId uniqueidentifier NOT NULL,PartnerRole varchar(30) NOT NULL CONSTRAINT DF_PartnerUsers_Role DEFAULT 'STAFF',IsPrimary bit NOT NULL CONSTRAINT DF_PartnerUsers_Primary DEFAULT 0,IsActive bit NOT NULL CONSTRAINT DF_PartnerUsers_Active DEFAULT 1,CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_PartnerUsers_Created DEFAULT SYSUTCDATETIME(),CONSTRAINT PK_PartnerUsers PRIMARY KEY(PartnerId,UserId),CONSTRAINT FK_PartnerUsers_Partner FOREIGN KEY(PartnerId) REFERENCES dbo.Partners(Id),CONSTRAINT FK_PartnerUsers_User FOREIGN KEY(UserId) REFERENCES dbo.Users(Id),CONSTRAINT CK_PartnerUsers_Role CHECK(PartnerRole IN('OWNER','ADMIN','ACCOUNTANT','STAFF','VIEWER')));
END
GO
IF COL_LENGTH('dbo.Companies','PartnerId') IS NULL ALTER TABLE dbo.Companies ADD PartnerId uniqueidentifier NULL;
GO
IF COL_LENGTH('dbo.Customers','PartnerId') IS NULL ALTER TABLE dbo.Customers ADD PartnerId uniqueidentifier NULL;
GO
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name='FK_Companies_Partner') ALTER TABLE dbo.Companies ADD CONSTRAINT FK_Companies_Partner FOREIGN KEY(PartnerId) REFERENCES dbo.Partners(Id);
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name='FK_Customers_Partner') ALTER TABLE dbo.Customers ADD CONSTRAINT FK_Customers_Partner FOREIGN KEY(PartnerId) REFERENCES dbo.Partners(Id);
GO
IF NOT EXISTS(SELECT 1 FROM dbo.Partners WHERE Code='TA') INSERT dbo.Partners(Code,PartnerType,Name,LegalName,Status,IsPlatformOwner) VALUES('TA','ENTERPRISE',N'T.A',N'T.A', 'ACTIVE',1);
DECLARE @TA uniqueidentifier=(SELECT TOP 1 Id FROM dbo.Partners WHERE Code='TA' AND IsDeleted=0);
UPDATE dbo.Companies SET PartnerId=@TA WHERE PartnerId IS NULL; UPDATE dbo.Customers SET PartnerId=@TA WHERE PartnerId IS NULL;
INSERT dbo.PartnerUsers(PartnerId,UserId,PartnerRole,IsPrimary) SELECT @TA,u.Id,'ADMIN',1 FROM dbo.Users u WHERE u.IsDeleted=0 AND NOT EXISTS(SELECT 1 FROM dbo.PartnerUsers pu WHERE pu.PartnerId=@TA AND pu.UserId=u.Id);
GO
PRINT '009-partner-tenant-foundation.sql: OK';
GO