/* RC2 031 - Separate receivable from payment instruction/code/QR lifecycle. */
USE [TAPortal]; GO
IF OBJECT_ID(N'dbo.PaymentInstructions',N'U') IS NULL BEGIN
 CREATE TABLE dbo.PaymentInstructions(Id uniqueidentifier NOT NULL DEFAULT NEWSEQUENTIALID() CONSTRAINT PK_PaymentInstructions PRIMARY KEY,PartnerId uniqueidentifier NOT NULL,PaymentRequestId uniqueidentifier NOT NULL,FinancialAccountId uniqueidentifier NULL,InstructionType varchar(20) NOT NULL,Amount decimal(18,2) NULL,CurrencyCode char(3) NOT NULL DEFAULT 'VND',PaymentCode nvarchar(150) NULL,PaymentCodeNormalized nvarchar(150) NULL,QrProvider varchar(50) NULL,QrPayload nvarchar(2000) NULL,ValidFrom datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),ExpiresAt datetime2(3) NULL,Status varchar(20) NOT NULL DEFAULT 'ACTIVE',CreatedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),RevokedAt datetime2(3) NULL,CONSTRAINT FK_PI_Partner FOREIGN KEY(PartnerId) REFERENCES dbo.Partners(Id),CONSTRAINT FK_PI_Request FOREIGN KEY(PaymentRequestId) REFERENCES dbo.PaymentRequests(Id),CONSTRAINT FK_PI_Account FOREIGN KEY(FinancialAccountId) REFERENCES dbo.FinancialAccounts(Id),CONSTRAINT CK_PI_Type CHECK(InstructionType IN('TRANSFER','QR','LINK','MANUAL')),CONSTRAINT CK_PI_Status CHECK(Status IN('ACTIVE','EXPIRED','REVOKED','CONSUMED')));
 CREATE INDEX IX_PI_Request ON dbo.PaymentInstructions(PaymentRequestId,CreatedAt DESC);
END
IF OBJECT_ID(N'dbo.PaymentCodeRegistry',N'U') IS NULL BEGIN
 CREATE TABLE dbo.PaymentCodeRegistry(Id uniqueidentifier NOT NULL DEFAULT NEWSEQUENTIALID() CONSTRAINT PK_PaymentCodeRegistry PRIMARY KEY,PartnerId uniqueidentifier NOT NULL,PaymentInstructionId uniqueidentifier NOT NULL,Code nvarchar(150) NOT NULL,NormalizedCode nvarchar(150) NOT NULL,IssuedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),CONSTRAINT FK_PCR_Partner FOREIGN KEY(PartnerId) REFERENCES dbo.Partners(Id),CONSTRAINT FK_PCR_Instruction FOREIGN KEY(PaymentInstructionId) REFERENCES dbo.PaymentInstructions(Id));
 CREATE UNIQUE INDEX UX_PCR_PartnerCode ON dbo.PaymentCodeRegistry(PartnerId,NormalizedCode);
END
IF COL_LENGTH('dbo.PaymentRequests','DueAt') IS NULL ALTER TABLE dbo.PaymentRequests ADD DueAt datetime2(3) NULL;
IF COL_LENGTH('dbo.PaymentRequests','ExternalReference') IS NULL ALTER TABLE dbo.PaymentRequests ADD ExternalReference nvarchar(200) NULL;
IF COL_LENGTH('dbo.PaymentRequests','CancelledReason') IS NULL ALTER TABLE dbo.PaymentRequests ADD CancelledReason nvarchar(500) NULL;
GO
PRINT '031-payment-instruction-code-registry.sql: OK'; GO