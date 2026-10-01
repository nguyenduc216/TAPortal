/* TAPortal 016 - SePay Bank Hub sandbox/production environment model.
   Verified against SePay Bank Hub public sandbox docs on 2026-09-30.
   No client_secret/token is stored by this migration. */
USE [TAPortal];
GO

IF COL_LENGTH('dbo.PartnerProviderConnections','Environment') IS NULL
    ALTER TABLE dbo.PartnerProviderConnections ADD Environment varchar(20) NOT NULL CONSTRAINT DF_PPC_Environment DEFAULT 'SANDBOX';
GO
IF COL_LENGTH('dbo.PartnerProviderConnections','BaseUrl') IS NULL
    ALTER TABLE dbo.PartnerProviderConnections ADD BaseUrl nvarchar(500) NULL;
GO
IF EXISTS(SELECT 1 FROM sys.key_constraints WHERE parent_object_id=OBJECT_ID('dbo.PartnerProviderConnections') AND name='UQ_PPC')
    ALTER TABLE dbo.PartnerProviderConnections DROP CONSTRAINT UQ_PPC;
GO
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('dbo.PartnerProviderConnections') AND name='CK_PPC_Environment')
    ALTER TABLE dbo.PartnerProviderConnections ADD CONSTRAINT CK_PPC_Environment CHECK(Environment IN('SANDBOX','PRODUCTION'));
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.PartnerProviderConnections') AND name='UX_PPC_PartnerProviderEnvironment')
    CREATE UNIQUE INDEX UX_PPC_PartnerProviderEnvironment ON dbo.PartnerProviderConnections(PartnerId,ProviderId,Environment);
GO

IF OBJECT_ID(N'dbo.ProviderWebhookConfigs',N'U') IS NULL BEGIN
 CREATE TABLE dbo.ProviderWebhookConfigs(
  Id uniqueidentifier NOT NULL CONSTRAINT PK_ProviderWebhookConfigs PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
  PartnerProviderConnectionId uniqueidentifier NOT NULL,
  WebhookType varchar(30) NOT NULL,
  EndpointPath nvarchar(500) NOT NULL,
  ExternalWebhookId nvarchar(200) NULL,
  SecretReference nvarchar(500) NULL,
  IsActive bit NOT NULL CONSTRAINT DF_PWC_Active DEFAULT 1,
  LastVerifiedAt datetime2(3) NULL,
  CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_PWC_Created DEFAULT SYSUTCDATETIME(),
  UpdatedAt datetime2(3) NULL,
  RowVersion rowversion NOT NULL,
  CONSTRAINT FK_PWC_Connection FOREIGN KEY(PartnerProviderConnectionId) REFERENCES dbo.PartnerProviderConnections(Id),
  CONSTRAINT CK_PWC_Type CHECK(WebhookType IN('TRANSACTION_IPN','BANK_ACCOUNT_EVENT'))
 );
 CREATE UNIQUE INDEX UX_PWC_ConnectionType ON dbo.ProviderWebhookConfigs(PartnerProviderConnectionId,WebhookType);
END
GO

IF OBJECT_ID(N'dbo.ProviderApiLogs',N'U') IS NULL BEGIN
 CREATE TABLE dbo.ProviderApiLogs(
  Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProviderApiLogs PRIMARY KEY,
  PartnerProviderConnectionId uniqueidentifier NULL,
  CorrelationId uniqueidentifier NOT NULL CONSTRAINT DF_PAL_Correlation DEFAULT NEWID(),
  Operation varchar(100) NOT NULL,
  HttpMethod varchar(10) NULL,
  RequestPath nvarchar(1000) NULL,
  HttpStatus int NULL,
  Success bit NOT NULL,
  DurationMs int NULL,
  RequestBodySanitized nvarchar(max) NULL,
  ResponseBodySanitized nvarchar(max) NULL,
  ErrorCode nvarchar(100) NULL,
  ErrorMessage nvarchar(2000) NULL,
  CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_PAL_Created DEFAULT SYSUTCDATETIME(),
  CONSTRAINT FK_PAL_Connection FOREIGN KEY(PartnerProviderConnectionId) REFERENCES dbo.PartnerProviderConnections(Id)
 );
 CREATE INDEX IX_ProviderApiLogs_ConnectionCreated ON dbo.ProviderApiLogs(PartnerProviderConnectionId,CreatedAt DESC);
END
GO

IF OBJECT_ID(N'dbo.BankProviderCatalog',N'U') IS NULL BEGIN
 CREATE TABLE dbo.BankProviderCatalog(
  Id uniqueidentifier NOT NULL CONSTRAINT PK_BankProviderCatalog PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
  ProviderId uniqueidentifier NOT NULL,
  BankCode varchar(30) NOT NULL,
  BankName nvarchar(200) NOT NULL,
  SandboxSupported bit NOT NULL DEFAULT 0,
  IsActive bit NOT NULL DEFAULT 1,
  UpdatedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
  CONSTRAINT FK_BPC_Provider FOREIGN KEY(ProviderId) REFERENCES dbo.Providers(Id)
 );
 CREATE UNIQUE INDEX UX_BPC_ProviderBank ON dbo.BankProviderCatalog(ProviderId,BankCode);
END
GO

DECLARE @SePay uniqueidentifier=(SELECT TOP 1 Id FROM dbo.Providers WHERE Code='SEPAY_BANKHUB');
IF @SePay IS NOT NULL BEGIN
 DECLARE @B TABLE(BankCode varchar(30),BankName nvarchar(200),SandboxSupported bit);
 INSERT @B VALUES('MB',N'MBBank',1),('ACB',N'ACB',1),('BIDV',N'BIDV',1),('VIETINBANK',N'VietinBank',1),('OCB',N'OCB',1),('KIENLONGBANK',N'KienLongBank',1),('VPBANK',N'VPBank',1),('SACOMBANK',N'Sacombank',1);
 UPDATE t SET BankName=s.BankName,SandboxSupported=s.SandboxSupported,IsActive=1,UpdatedAt=SYSUTCDATETIME() FROM dbo.BankProviderCatalog t JOIN @B s ON s.BankCode=t.BankCode WHERE t.ProviderId=@SePay;
 INSERT dbo.BankProviderCatalog(ProviderId,BankCode,BankName,SandboxSupported) SELECT @SePay,s.BankCode,s.BankName,s.SandboxSupported FROM @B s WHERE NOT EXISTS(SELECT 1 FROM dbo.BankProviderCatalog t WHERE t.ProviderId=@SePay AND t.BankCode=s.BankCode);
END
GO

UPDATE ppc SET BaseUrl=CASE Environment WHEN 'SANDBOX' THEN 'https://bankhub-api-sandbox.sepay.vn/v1' ELSE 'https://bankhub-api.sepay.vn/v1' END
FROM dbo.PartnerProviderConnections ppc JOIN dbo.Providers p ON p.Id=ppc.ProviderId
WHERE p.Code='SEPAY_BANKHUB' AND (ppc.BaseUrl IS NULL OR ppc.BaseUrl='');
GO
PRINT '016-sepay-bankhub-environments.sql: OK';
GO