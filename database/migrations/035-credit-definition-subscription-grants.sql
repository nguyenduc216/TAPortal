/* RC2 035 - Service-based credit definitions; wallet is sole consumable quota source. */
USE [TAPortal]; GO
IF OBJECT_ID(N'dbo.CreditDefinitions',N'U') IS NULL BEGIN
 CREATE TABLE dbo.CreditDefinitions(Id uniqueidentifier NOT NULL DEFAULT NEWSEQUENTIALID() CONSTRAINT PK_CreditDefinitions PRIMARY KEY,ServiceId uniqueidentifier NOT NULL,Code varchar(50) NOT NULL,Name nvarchar(200) NOT NULL,Unit varchar(30) NOT NULL,PrecisionScale tinyint NOT NULL DEFAULT 0,ExpiryPolicy varchar(20) NOT NULL DEFAULT 'PER_GRANT',IsActive bit NOT NULL DEFAULT 1,CreatedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),CONSTRAINT FK_CD_Service FOREIGN KEY(ServiceId) REFERENCES dbo.Services(Id),CONSTRAINT CK_CD_Scale CHECK(PrecisionScale BETWEEN 0 AND 4),CONSTRAINT CK_CD_Expiry CHECK(ExpiryPolicy IN('NONE','PER_GRANT','PER_SUBSCRIPTION')));
 CREATE UNIQUE INDEX UX_CD_Code ON dbo.CreditDefinitions(Code);
END
IF NOT EXISTS(SELECT 1 FROM dbo.CreditDefinitions WHERE Code='BANK_TRANSACTION_UNIT') INSERT dbo.CreditDefinitions(ServiceId,Code,Name,Unit,PrecisionScale,ExpiryPolicy) SELECT Id,'BANK_TRANSACTION_UNIT',N'Đơn vị giao dịch ngân hàng','TRANSACTION',0,'PER_SUBSCRIPTION' FROM dbo.Services WHERE Code='BANKING';
IF NOT EXISTS(SELECT 1 FROM dbo.CreditDefinitions WHERE Code='INVOICE_ISSUE_UNIT') INSERT dbo.CreditDefinitions(ServiceId,Code,Name,Unit,PrecisionScale,ExpiryPolicy) SELECT Id,'INVOICE_ISSUE_UNIT',N'Đơn vị phát hành hóa đơn','INVOICE',0,'PER_SUBSCRIPTION' FROM dbo.Services WHERE Code='EINVOICE';
GO
IF COL_LENGTH('dbo.PartnerCreditWallets','CreditDefinitionId') IS NULL ALTER TABLE dbo.PartnerCreditWallets ADD CreditDefinitionId uniqueidentifier NULL;
IF COL_LENGTH('dbo.CreditLedger','CreditDefinitionId') IS NULL ALTER TABLE dbo.CreditLedger ADD CreditDefinitionId uniqueidentifier NULL;
IF COL_LENGTH('dbo.CreditReservations','CreditDefinitionId') IS NULL ALTER TABLE dbo.CreditReservations ADD CreditDefinitionId uniqueidentifier NULL;
IF COL_LENGTH('dbo.CreditUsageRules','CreditDefinitionId') IS NULL ALTER TABLE dbo.CreditUsageRules ADD CreditDefinitionId uniqueidentifier NULL;
IF COL_LENGTH('dbo.CreditTopups','CreditDefinitionId') IS NULL ALTER TABLE dbo.CreditTopups ADD CreditDefinitionId uniqueidentifier NULL;
GO
UPDATE w SET CreditDefinitionId=d.Id FROM dbo.PartnerCreditWallets w JOIN dbo.CreditDefinitions d ON d.Code=CASE w.CreditType WHEN 'BANKING' THEN 'BANK_TRANSACTION_UNIT' WHEN 'EINVOICE' THEN 'INVOICE_ISSUE_UNIT' END WHERE w.CreditDefinitionId IS NULL;
UPDATE l SET CreditDefinitionId=d.Id FROM dbo.CreditLedger l JOIN dbo.CreditDefinitions d ON d.Code=CASE l.CreditType WHEN 'BANKING' THEN 'BANK_TRANSACTION_UNIT' WHEN 'EINVOICE' THEN 'INVOICE_ISSUE_UNIT' END WHERE l.CreditDefinitionId IS NULL;
UPDATE r SET CreditDefinitionId=d.Id FROM dbo.CreditReservations r JOIN dbo.CreditDefinitions d ON d.Code=CASE r.CreditType WHEN 'BANKING' THEN 'BANK_TRANSACTION_UNIT' WHEN 'EINVOICE' THEN 'INVOICE_ISSUE_UNIT' END WHERE r.CreditDefinitionId IS NULL;
UPDATE r SET CreditDefinitionId=d.Id FROM dbo.CreditUsageRules r JOIN dbo.CreditDefinitions d ON d.Code=CASE r.CreditType WHEN 'BANKING' THEN 'BANK_TRANSACTION_UNIT' WHEN 'EINVOICE' THEN 'INVOICE_ISSUE_UNIT' END WHERE r.CreditDefinitionId IS NULL;
UPDATE t SET CreditDefinitionId=d.Id FROM dbo.CreditTopups t JOIN dbo.CreditDefinitions d ON d.Code=CASE t.CreditType WHEN 'BANKING' THEN 'BANK_TRANSACTION_UNIT' WHEN 'EINVOICE' THEN 'INVOICE_ISSUE_UNIT' END WHERE t.CreditDefinitionId IS NULL;
GO
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name='FK_Wallet_CreditDefinition') ALTER TABLE dbo.PartnerCreditWallets ADD CONSTRAINT FK_Wallet_CreditDefinition FOREIGN KEY(CreditDefinitionId) REFERENCES dbo.CreditDefinitions(Id);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.PartnerCreditWallets') AND name='UX_Wallet_Definition') CREATE UNIQUE INDEX UX_Wallet_Definition ON dbo.PartnerCreditWallets(PartnerId,CreditDefinitionId) WHERE CreditDefinitionId IS NOT NULL;
GO
IF OBJECT_ID(N'dbo.SubscriptionPeriods',N'U') IS NULL CREATE TABLE dbo.SubscriptionPeriods(Id uniqueidentifier NOT NULL DEFAULT NEWSEQUENTIALID() CONSTRAINT PK_SubscriptionPeriods PRIMARY KEY,PartnerSubscriptionId uniqueidentifier NOT NULL,PeriodStart datetime2(3) NOT NULL,PeriodEnd datetime2(3) NOT NULL,Status varchar(20) NOT NULL DEFAULT 'ACTIVE',CreatedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),CONSTRAINT FK_SP_Sub FOREIGN KEY(PartnerSubscriptionId) REFERENCES dbo.PartnerSubscriptions(Id),CONSTRAINT CK_SP_Range CHECK(PeriodEnd>PeriodStart),CONSTRAINT CK_SP_Status CHECK(Status IN('PENDING','ACTIVE','CLOSED','CANCELLED')));
IF OBJECT_ID(N'dbo.SubscriptionCreditGrants',N'U') IS NULL BEGIN
 CREATE TABLE dbo.SubscriptionCreditGrants(Id uniqueidentifier NOT NULL DEFAULT NEWSEQUENTIALID() CONSTRAINT PK_SubscriptionCreditGrants PRIMARY KEY,SubscriptionPeriodId uniqueidentifier NOT NULL,PartnerId uniqueidentifier NOT NULL,CreditDefinitionId uniqueidentifier NOT NULL,Quantity decimal(18,2) NOT NULL,IdempotencyKey nvarchar(200) NOT NULL,Status varchar(20) NOT NULL DEFAULT 'PENDING',CreditLedgerId bigint NULL,CreatedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),GrantedAt datetime2(3) NULL,CONSTRAINT FK_SCG_Period FOREIGN KEY(SubscriptionPeriodId) REFERENCES dbo.SubscriptionPeriods(Id),CONSTRAINT FK_SCG_Partner FOREIGN KEY(PartnerId) REFERENCES dbo.Partners(Id),CONSTRAINT FK_SCG_Def FOREIGN KEY(CreditDefinitionId) REFERENCES dbo.CreditDefinitions(Id),CONSTRAINT FK_SCG_Ledger FOREIGN KEY(CreditLedgerId) REFERENCES dbo.CreditLedger(Id),CONSTRAINT CK_SCG_Qty CHECK(Quantity>0),CONSTRAINT CK_SCG_Status CHECK(Status IN('PENDING','GRANTED','CANCELLED','FAILED')));
 CREATE UNIQUE INDEX UX_SCG_Idem ON dbo.SubscriptionCreditGrants(PartnerId,IdempotencyKey);
END
GO
PRINT '035-credit-definition-subscription-grants.sql: OK'; GO