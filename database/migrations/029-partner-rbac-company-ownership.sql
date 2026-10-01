/* RC2 029 - Partner membership RBAC + legal Company ownership. SQL Server 2014 compatible. */
USE [TAPortal];
GO
IF COL_LENGTH('dbo.PartnerUsers','Status') IS NULL ALTER TABLE dbo.PartnerUsers ADD Status varchar(20) NOT NULL CONSTRAINT DF_PU_Status DEFAULT 'ACTIVE';
IF COL_LENGTH('dbo.PartnerUsers','JoinedAt') IS NULL ALTER TABLE dbo.PartnerUsers ADD JoinedAt datetime2(3) NULL;
IF COL_LENGTH('dbo.PartnerUsers','LeftAt') IS NULL ALTER TABLE dbo.PartnerUsers ADD LeftAt datetime2(3) NULL;
GO
IF OBJECT_ID(N'dbo.PartnerRoles',N'U') IS NULL BEGIN
 CREATE TABLE dbo.PartnerRoles(Id uniqueidentifier NOT NULL DEFAULT NEWSEQUENTIALID() CONSTRAINT PK_PartnerRoles PRIMARY KEY,PartnerId uniqueidentifier NOT NULL,Code varchar(50) NOT NULL,Name nvarchar(200) NOT NULL,IsSystemRole bit NOT NULL DEFAULT 0,IsActive bit NOT NULL DEFAULT 1,CreatedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),CONSTRAINT FK_PRoles_Partner FOREIGN KEY(PartnerId) REFERENCES dbo.Partners(Id));
 CREATE UNIQUE INDEX UX_PartnerRoles_Code ON dbo.PartnerRoles(PartnerId,Code);
END
IF OBJECT_ID(N'dbo.PartnerRolePermissions',N'U') IS NULL CREATE TABLE dbo.PartnerRolePermissions(PartnerRoleId uniqueidentifier NOT NULL,PermissionId uniqueidentifier NOT NULL,CONSTRAINT PK_PartnerRolePermissions PRIMARY KEY(PartnerRoleId,PermissionId),CONSTRAINT FK_PRP_Role FOREIGN KEY(PartnerRoleId) REFERENCES dbo.PartnerRoles(Id),CONSTRAINT FK_PRP_Permission FOREIGN KEY(PermissionId) REFERENCES dbo.Permissions(Id));
IF OBJECT_ID(N'dbo.PartnerUserRoles',N'U') IS NULL CREATE TABLE dbo.PartnerUserRoles(PartnerId uniqueidentifier NOT NULL,UserId uniqueidentifier NOT NULL,PartnerRoleId uniqueidentifier NOT NULL,AssignedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),AssignedBy uniqueidentifier NULL,CONSTRAINT PK_PartnerUserRoles PRIMARY KEY(PartnerId,UserId,PartnerRoleId),CONSTRAINT FK_PUR_Membership FOREIGN KEY(PartnerId,UserId) REFERENCES dbo.PartnerUsers(PartnerId,UserId),CONSTRAINT FK_PUR_Role FOREIGN KEY(PartnerRoleId) REFERENCES dbo.PartnerRoles(Id),CONSTRAINT FK_PUR_AssignedBy FOREIGN KEY(AssignedBy) REFERENCES dbo.Users(Id));
GO
IF COL_LENGTH('dbo.PartnerBankAccounts','CompanyId') IS NULL ALTER TABLE dbo.PartnerBankAccounts ADD CompanyId uniqueidentifier NULL;
IF COL_LENGTH('dbo.PaymentRequests','CompanyId') IS NULL ALTER TABLE dbo.PaymentRequests ADD CompanyId uniqueidentifier NULL;
IF COL_LENGTH('dbo.InvoiceRequests','CompanyId') IS NULL ALTER TABLE dbo.InvoiceRequests ADD CompanyId uniqueidentifier NULL;
GO
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name='FK_PBA_Company') ALTER TABLE dbo.PartnerBankAccounts ADD CONSTRAINT FK_PBA_Company FOREIGN KEY(CompanyId) REFERENCES dbo.Companies(Id);
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name='FK_PReq_Company') ALTER TABLE dbo.PaymentRequests ADD CONSTRAINT FK_PReq_Company FOREIGN KEY(CompanyId) REFERENCES dbo.Companies(Id);
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name='FK_IReq_Company') ALTER TABLE dbo.InvoiceRequests ADD CONSTRAINT FK_IReq_Company FOREIGN KEY(CompanyId) REFERENCES dbo.Companies(Id);
GO
PRINT '029-partner-rbac-company-ownership.sql: OK';
GO