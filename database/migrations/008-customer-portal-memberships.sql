-- Manual execution only on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys.
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
-- Equivalent required positive guard: DB_NAME() = 'TAPortalSys'.
-- Run only after read-only schema audit against an explicitly selected, backed-up database.
-- No USE statement: caller controls database. No automatic application startup migrations.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.Users','U') IS NULL OR OBJECT_ID('dbo.Customers','U') IS NULL
 THROW 51000,'Audit required: Users/Customers missing',1;
IF COL_LENGTH('dbo.Users','IsDeleted') IS NULL OR COL_LENGTH('dbo.Customers','IsActive') IS NULL
 THROW 51000,'Audit required: incompatible schema',1;
IF OBJECT_ID('dbo.CustomerPortalMemberships','U') IS NULL
BEGIN
 CREATE TABLE dbo.CustomerPortalMemberships (
 Id uniqueidentifier NOT NULL CONSTRAINT PK_CustomerPortalMemberships PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
 UserId uniqueidentifier NOT NULL,CustomerId uniqueidentifier NOT NULL,
 PortalRole varchar(30) NOT NULL,
 IsActive bit NOT NULL CONSTRAINT DF_CPM_Active DEFAULT 1,
 CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_CPM_Created DEFAULT SYSUTCDATETIME(),
 CreatedBy uniqueidentifier NULL,UpdatedAt datetime2(3) NULL,UpdatedBy uniqueidentifier NULL,
 CONSTRAINT FK_CPM_User FOREIGN KEY(UserId) REFERENCES dbo.Users(Id),
 CONSTRAINT FK_CPM_Customer FOREIGN KEY(CustomerId) REFERENCES dbo.Customers(Id),
 CONSTRAINT CK_CPM_Role CHECK(PortalRole IN ('OWNER','ADMIN','VIEWER')),
 CONSTRAINT UQ_CPM_UserCustomer UNIQUE(UserId,CustomerId));
 CREATE INDEX IX_CPM_Customer ON dbo.CustomerPortalMemberships(CustomerId,IsActive) INCLUDE(UserId,PortalRole);
END;
-- Explicitly seed an admin permission, never assign system roles to customer users.
IF NOT EXISTS(SELECT 1 FROM dbo.Permissions WHERE Code='CUSTOMERS.PORTAL.MANAGE' AND IsDeleted=0)
 INSERT dbo.Permissions(Id,Code,Name,ActionCode,IsSystem,IsActive) VALUES(NEWID(),'CUSTOMERS.PORTAL.MANAGE',N'Manage customer portal accounts','MANAGE',1,1);
IF NOT EXISTS(SELECT 1 FROM dbo.Permissions WHERE Code='CUSTOMERS.VIEW' AND IsDeleted=0)
 INSERT dbo.Permissions(Id,Code,Name,ActionCode,IsSystem,IsActive) VALUES(NEWID(),'CUSTOMERS.VIEW',N'View administration overview and customers','VIEW',1,1);
COMMIT;
