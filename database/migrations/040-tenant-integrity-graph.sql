/* V1 RC3 040 - tenant integrity graph. */
USE [TAPortal];
GO
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.PartnerRoles') AND name='UX_PRoles_PartnerId_Id') CREATE UNIQUE INDEX UX_PRoles_PartnerId_Id ON dbo.PartnerRoles(PartnerId,Id);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.FinancialAccounts') AND name='UX_FA_PartnerId_Id') CREATE UNIQUE INDEX UX_FA_PartnerId_Id ON dbo.FinancialAccounts(PartnerId,Id);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.PaymentInstructions') AND name='UX_PI_PartnerId_Id') CREATE UNIQUE INDEX UX_PI_PartnerId_Id ON dbo.PaymentInstructions(PartnerId,Id);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.CustomerCashAccounts') AND name='UX_CCA_PartnerId_Id') CREATE UNIQUE INDEX UX_CCA_PartnerId_Id ON dbo.CustomerCashAccounts(PartnerId,Id);
GO
IF COL_LENGTH('dbo.PartnerUserRoles','RolePartnerId') IS NULL ALTER TABLE dbo.PartnerUserRoles ADD RolePartnerId uniqueidentifier NULL;
GO
UPDATE dbo.PartnerUserRoles SET RolePartnerId=PartnerId WHERE RolePartnerId IS NULL;
ALTER TABLE dbo.PartnerUserRoles ALTER COLUMN RolePartnerId uniqueidentifier NOT NULL;
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name='FK_PUR_TenantRole') ALTER TABLE dbo.PartnerUserRoles ADD CONSTRAINT FK_PUR_TenantRole FOREIGN KEY(RolePartnerId,PartnerRoleId) REFERENCES dbo.PartnerRoles(PartnerId,Id);
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name='CK_PUR_SamePartner') ALTER TABLE dbo.PartnerUserRoles ADD CONSTRAINT CK_PUR_SamePartner CHECK(RolePartnerId=PartnerId);
GO
IF COL_LENGTH('dbo.PaymentInstructions','RequestPartnerId') IS NULL ALTER TABLE dbo.PaymentInstructions ADD RequestPartnerId uniqueidentifier NULL;
GO
UPDATE dbo.PaymentInstructions SET RequestPartnerId=PartnerId WHERE RequestPartnerId IS NULL;
ALTER TABLE dbo.PaymentInstructions ALTER COLUMN RequestPartnerId uniqueidentifier NOT NULL;
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name='FK_PI_TenantRequest') ALTER TABLE dbo.PaymentInstructions ADD CONSTRAINT FK_PI_TenantRequest FOREIGN KEY(RequestPartnerId,PaymentRequestId) REFERENCES dbo.PaymentRequests(PartnerId,Id);
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name='CK_PI_SamePartner') ALTER TABLE dbo.PaymentInstructions ADD CONSTRAINT CK_PI_SamePartner CHECK(RequestPartnerId=PartnerId);
GO
PRINT '040-tenant-integrity-graph.sql: OK';
GO