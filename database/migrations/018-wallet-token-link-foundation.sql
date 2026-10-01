/* TAPortal 018 - Wallet, token lifecycle, provider link sessions and usage rules. */
USE [TAPortal];
GO
IF OBJECT_ID(N'dbo.ProviderTokenSessions',N'U') IS NULL BEGIN
 CREATE TABLE dbo.ProviderTokenSessions(
  Id uniqueidentifier NOT NULL CONSTRAINT PK_ProviderTokenSessions PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
  PartnerProviderConnectionId uniqueidentifier NOT NULL,
  TokenType varchar(30) NOT NULL,
  ExternalTokenId nvarchar(200) NULL,
  SecretReference nvarchar(500) NOT NULL,
  Status varchar(20) NOT NULL CONSTRAINT DF_PTS_Status DEFAULT 'ACTIVE',
  IssuedAt datetime2(3) NOT NULL CONSTRAINT DF_PTS_Issued DEFAULT SYSUTCDATETIME(),
  ExpiresAt datetime2(3) NULL,
  RevokedAt datetime2(3) NULL,
  LastUsedAt datetime2(3) NULL,
  CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_PTS_Created DEFAULT SYSUTCDATETIME(),
  RowVersion rowversion NOT NULL,
  CONSTRAINT FK_PTS_Connection FOREIGN KEY(PartnerProviderConnectionId) REFERENCES dbo.PartnerProviderConnections(Id),
  CONSTRAINT CK_PTS_Type CHECK(TokenType IN('ACCESS_TOKEN','LINK_TOKEN')),
  CONSTRAINT CK_PTS_Status CHECK(Status IN('ACTIVE','EXPIRED','REVOKED','CONSUMED','FAILED'))
 );
 CREATE INDEX IX_PTS_ConnectionTypeStatus ON dbo.ProviderTokenSessions(PartnerProviderConnectionId,TokenType,Status,ExpiresAt);
END
GO
IF OBJECT_ID(N'dbo.BankLinkSessions',N'U') IS NULL BEGIN
 CREATE TABLE dbo.BankLinkSessions(
  Id uniqueidentifier NOT NULL CONSTRAINT PK_BankLinkSessions PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
  PartnerId uniqueidentifier NOT NULL,
  PartnerProviderConnectionId uniqueidentifier NOT NULL,
  ProviderTokenSessionId uniqueidentifier NULL,
  Purpose varchar(20) NOT NULL,
  ExternalLinkXid nvarchar(200) NULL,
  ExternalBankAccountId nvarchar(200) NULL,
  HostedLinkUrl nvarchar(2000) NULL,
  RedirectUrl nvarchar(2000) NULL,
  Language varchar(10) NULL,
  IsMobile bit NOT NULL DEFAULT 0,
  Status varchar(30) NOT NULL DEFAULT 'CREATED',
  ExpiresAt datetime2(3) NULL,
  CompletedAt datetime2(3) NULL,
  ErrorCode nvarchar(100) NULL,
  ErrorMessage nvarchar(2000) NULL,
  CreatedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
  UpdatedAt datetime2(3) NULL,
  RowVersion rowversion NOT NULL,
  CONSTRAINT FK_BLS_Partner FOREIGN KEY(PartnerId) REFERENCES dbo.Partners(Id),
  CONSTRAINT FK_BLS_Connection FOREIGN KEY(PartnerProviderConnectionId) REFERENCES dbo.PartnerProviderConnections(Id),
  CONSTRAINT FK_BLS_Token FOREIGN KEY(ProviderTokenSessionId) REFERENCES dbo.ProviderTokenSessions(Id),
  CONSTRAINT CK_BLS_Purpose CHECK(Purpose IN('LINK','UNLINK','REACTIVATE')),
  CONSTRAINT CK_BLS_Status CHECK(Status IN('CREATED','OPENED','COMPLETED','EXPIRED','CANCELLED','FAILED'))
 );
 CREATE INDEX IX_BLS_PartnerCreated ON dbo.BankLinkSessions(PartnerId,CreatedAt DESC);
END
GO
IF OBJECT_ID(N'dbo.CreditUsageRules',N'U') IS NULL BEGIN
 CREATE TABLE dbo.CreditUsageRules(
  Id uniqueidentifier NOT NULL CONSTRAINT PK_CreditUsageRules PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
  CreditType varchar(20) NOT NULL,
  UsageEvent varchar(50) NOT NULL,
  Quantity decimal(18,4) NOT NULL,
  EffectiveFrom datetime2(3) NOT NULL,
  EffectiveTo datetime2(3) NULL,
  IsActive bit NOT NULL DEFAULT 1,
  Description nvarchar(500) NULL,
  CreatedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
  CONSTRAINT CK_CUR_Type CHECK(CreditType IN('BANKING','EINVOICE')),
  CONSTRAINT CK_CUR_Qty CHECK(Quantity>=0)
 );
 CREATE INDEX IX_CUR_Effective ON dbo.CreditUsageRules(CreditType,UsageEvent,EffectiveFrom DESC);
END
GO
IF OBJECT_ID(N'dbo.CreditReservations',N'U') IS NULL BEGIN
 CREATE TABLE dbo.CreditReservations(
  Id uniqueidentifier NOT NULL CONSTRAINT PK_CreditReservations PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
  PartnerId uniqueidentifier NOT NULL,
  CreditType varchar(20) NOT NULL,
  Quantity decimal(18,2) NOT NULL,
  PurposeType varchar(50) NOT NULL,
  PurposeId nvarchar(200) NOT NULL,
  Status varchar(20) NOT NULL DEFAULT 'ACTIVE',
  ExpiresAt datetime2(3) NULL,
  CreatedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
  ConsumedAt datetime2(3) NULL,
  ReleasedAt datetime2(3) NULL,
  RowVersion rowversion NOT NULL,
  CONSTRAINT FK_CR_Partner FOREIGN KEY(PartnerId) REFERENCES dbo.Partners(Id),
  CONSTRAINT CK_CR_Type CHECK(CreditType IN('BANKING','EINVOICE')),
  CONSTRAINT CK_CR_Status CHECK(Status IN('ACTIVE','CONSUMED','RELEASED','EXPIRED')),
  CONSTRAINT CK_CR_Qty CHECK(Quantity>0)
 );
 CREATE UNIQUE INDEX UX_CR_ActivePurpose ON dbo.CreditReservations(PartnerId,CreditType,PurposeType,PurposeId) WHERE Status='ACTIVE';
END
GO
IF OBJECT_ID(N'dbo.vw_PartnerCreditBalances',N'V') IS NULL EXEC('CREATE VIEW dbo.vw_PartnerCreditBalances AS SELECT 1 AS Placeholder');
GO
ALTER VIEW dbo.vw_PartnerCreditBalances AS
SELECT w.Id,w.PartnerId,w.CreditType,w.CurrentBalance,w.ReservedBalance,
 CAST(w.CurrentBalance-w.ReservedBalance AS decimal(18,2)) AvailableBalance
FROM dbo.PartnerCreditWallets w;
GO
PRINT '018-wallet-token-link-foundation.sql: OK';
GO