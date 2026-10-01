/* TAPortal 026 - Payment code lifecycle and matching controls. */
USE [TAPortal];
GO
IF COL_LENGTH('dbo.PaymentRequests','PaymentCodeNormalized') IS NULL ALTER TABLE dbo.PaymentRequests ADD PaymentCodeNormalized varchar(100) NULL;
IF COL_LENGTH('dbo.PaymentRequests','ValidFrom') IS NULL ALTER TABLE dbo.PaymentRequests ADD ValidFrom datetime2(3) NOT NULL CONSTRAINT DF_PR_ValidFrom DEFAULT SYSUTCDATETIME();
IF COL_LENGTH('dbo.PaymentRequests','MatchPolicy') IS NULL ALTER TABLE dbo.PaymentRequests ADD MatchPolicy varchar(20) NOT NULL CONSTRAINT DF_PR_MatchPolicy DEFAULT 'STRICT';
GO
UPDATE dbo.PaymentRequests SET PaymentCodeNormalized=UPPER(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(PaymentCode)),' ',''),'-',''),'_','')) WHERE PaymentCodeNormalized IS NULL;
GO
IF EXISTS(SELECT 1 FROM dbo.PaymentRequests GROUP BY PartnerId,PaymentCodeNormalized HAVING COUNT(*)>1)
 THROW 51260,'Normalized payment-code collision detected. Resolve duplicate legacy codes before migration continues.',1;
ALTER TABLE dbo.PaymentRequests ALTER COLUMN PaymentCodeNormalized varchar(100) NOT NULL;
GO
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name='CK_PR_MatchPolicy') ALTER TABLE dbo.PaymentRequests ADD CONSTRAINT CK_PR_MatchPolicy CHECK(MatchPolicy IN('STRICT','ASSISTED','MANUAL'));
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.PaymentRequests') AND name='UX_PR_NormalizedPaymentCode') CREATE UNIQUE INDEX UX_PR_NormalizedPaymentCode ON dbo.PaymentRequests(PartnerId,PaymentCodeNormalized);
GO
IF OBJECT_ID(N'dbo.PaymentCodeAliases',N'U') IS NULL BEGIN
 CREATE TABLE dbo.PaymentCodeAliases(Id uniqueidentifier NOT NULL DEFAULT NEWSEQUENTIALID() CONSTRAINT PK_PaymentCodeAliases PRIMARY KEY,PartnerId uniqueidentifier NOT NULL,PaymentRequestId uniqueidentifier NOT NULL,Alias varchar(150) NOT NULL,AliasNormalized varchar(150) NOT NULL,AliasType varchar(30) NOT NULL,IsActive bit NOT NULL DEFAULT 1,CreatedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),CONSTRAINT FK_PCA_Partner FOREIGN KEY(PartnerId) REFERENCES dbo.Partners(Id),CONSTRAINT FK_PCA_Request FOREIGN KEY(PaymentRequestId) REFERENCES dbo.PaymentRequests(Id),CONSTRAINT CK_PCA_Type CHECK(AliasType IN('PROVIDER_PREFIX','BANK_FORMAT','LEGACY','MANUAL')));
 CREATE UNIQUE INDEX UX_PCA_PartnerAlias ON dbo.PaymentCodeAliases(PartnerId,AliasNormalized) WHERE IsActive=1;
END
GO
IF OBJECT_ID(N'dbo.PaymentMatchingRules',N'U') IS NULL BEGIN
 CREATE TABLE dbo.PaymentMatchingRules(Id uniqueidentifier NOT NULL DEFAULT NEWSEQUENTIALID() CONSTRAINT PK_PaymentMatchingRules PRIMARY KEY,PartnerId uniqueidentifier NOT NULL,Name nvarchar(200) NOT NULL,Priority int NOT NULL,RuleType varchar(30) NOT NULL,ConfigJson nvarchar(max) NULL,AutoAllocate bit NOT NULL DEFAULT 0,IsActive bit NOT NULL DEFAULT 1,EffectiveFrom datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),EffectiveTo datetime2(3) NULL,CreatedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),CONSTRAINT FK_PMR_Partner FOREIGN KEY(PartnerId) REFERENCES dbo.Partners(Id),CONSTRAINT CK_PMR_Type CHECK(RuleType IN('PAYMENT_CODE','CUSTOMER_BANK','CONTENT','CUSTOMER_CODE','AMOUNT_ASSIST')));
 CREATE INDEX IX_PMR_PartnerPriority ON dbo.PaymentMatchingRules(PartnerId,IsActive,Priority);
END
GO
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('dbo.TransactionMatches') AND name='CK_TM_Confidence') ALTER TABLE dbo.TransactionMatches ADD CONSTRAINT CK_TM_Confidence CHECK(Confidence IS NULL OR (Confidence>=0 AND Confidence<=100));
GO
PRINT '026-payment-code-matching-controls.sql: OK';
GO