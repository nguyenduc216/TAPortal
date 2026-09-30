/* TAPortal 022 - Subscription, entitlement, top-up and billing lifecycle. */
USE [TAPortal];
GO
IF COL_LENGTH('dbo.PartnerSubscriptions','AutoRenew') IS NULL ALTER TABLE dbo.PartnerSubscriptions ADD AutoRenew bit NOT NULL CONSTRAINT DF_Sub_AutoRenew DEFAULT 0;
IF COL_LENGTH('dbo.PartnerSubscriptions','CancelledAt') IS NULL ALTER TABLE dbo.PartnerSubscriptions ADD CancelledAt datetime2(3) NULL;
IF COL_LENGTH('dbo.PartnerSubscriptions','UpdatedAt') IS NULL ALTER TABLE dbo.PartnerSubscriptions ADD UpdatedAt datetime2(3) NULL;
GO
IF OBJECT_ID(N'dbo.SubscriptionEntitlements',N'U') IS NULL BEGIN
 CREATE TABLE dbo.SubscriptionEntitlements(Id uniqueidentifier NOT NULL DEFAULT NEWSEQUENTIALID() CONSTRAINT PK_SubscriptionEntitlements PRIMARY KEY,PartnerSubscriptionId uniqueidentifier NOT NULL,ServiceId uniqueidentifier NOT NULL,GrantedQuantity decimal(18,2) NULL,UsedQuantity decimal(18,2) NOT NULL DEFAULT 0,ValidFrom datetime2(3) NOT NULL,ValidTo datetime2(3) NULL,Status varchar(20) NOT NULL DEFAULT 'ACTIVE',CreatedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),RowVersion rowversion NOT NULL,CONSTRAINT FK_SE_Sub FOREIGN KEY(PartnerSubscriptionId) REFERENCES dbo.PartnerSubscriptions(Id),CONSTRAINT FK_SE_Service FOREIGN KEY(ServiceId) REFERENCES dbo.Services(Id),CONSTRAINT CK_SE_Status CHECK(Status IN('ACTIVE','EXHAUSTED','EXPIRED','CANCELLED')),CONSTRAINT CK_SE_Qty CHECK(GrantedQuantity IS NULL OR GrantedQuantity>=0));
 CREATE UNIQUE INDEX UX_SE_SubServicePeriod ON dbo.SubscriptionEntitlements(PartnerSubscriptionId,ServiceId,ValidFrom);
END
GO
IF OBJECT_ID(N'dbo.CreditTopups',N'U') IS NULL BEGIN
 CREATE TABLE dbo.CreditTopups(Id uniqueidentifier NOT NULL DEFAULT NEWSEQUENTIALID() CONSTRAINT PK_CreditTopups PRIMARY KEY,PartnerId uniqueidentifier NOT NULL,CreditType varchar(20) NOT NULL,Quantity decimal(18,2) NOT NULL,Amount decimal(18,2) NULL,CurrencyCode char(3) NOT NULL DEFAULT 'VND',Status varchar(20) NOT NULL DEFAULT 'PENDING',PaymentReference nvarchar(200) NULL,IdempotencyKey nvarchar(200) NULL,RequestedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),CompletedAt datetime2(3) NULL,ExpiresAt datetime2(3) NULL,CONSTRAINT FK_CT_Partner FOREIGN KEY(PartnerId) REFERENCES dbo.Partners(Id),CONSTRAINT CK_CT_Type CHECK(CreditType IN('BANKING','EINVOICE')),CONSTRAINT CK_CT_Status CHECK(Status IN('PENDING','PAID','GRANTED','CANCELLED','EXPIRED','FAILED')),CONSTRAINT CK_CT_Qty CHECK(Quantity>0));
 CREATE UNIQUE INDEX UX_CT_Idempotency ON dbo.CreditTopups(PartnerId,IdempotencyKey) WHERE IdempotencyKey IS NOT NULL;
END
GO
IF OBJECT_ID(N'dbo.PartnerServiceOverrides',N'U') IS NULL BEGIN
 CREATE TABLE dbo.PartnerServiceOverrides(Id uniqueidentifier NOT NULL DEFAULT NEWSEQUENTIALID() CONSTRAINT PK_PartnerServiceOverrides PRIMARY KEY,PartnerId uniqueidentifier NOT NULL,ServiceId uniqueidentifier NOT NULL,IsEnabled bit NOT NULL,QuantityLimit decimal(18,2) NULL,EffectiveFrom datetime2(3) NOT NULL,EffectiveTo datetime2(3) NULL,Reason nvarchar(500) NULL,CreatedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),CONSTRAINT FK_PSO_Partner FOREIGN KEY(PartnerId) REFERENCES dbo.Partners(Id),CONSTRAINT FK_PSO_Service FOREIGN KEY(ServiceId) REFERENCES dbo.Services(Id));
 CREATE INDEX IX_PSO_Effective ON dbo.PartnerServiceOverrides(PartnerId,ServiceId,EffectiveFrom DESC);
END
GO
CREATE OR ALTER VIEW dbo.vw_PartnerServiceEntitlements AS
SELECT s.PartnerId,sv.Code ServiceCode,sv.Name ServiceName,SUM(CASE WHEN e.Status='ACTIVE' THEN ISNULL(e.GrantedQuantity,0) ELSE 0 END) GrantedQuantity,SUM(CASE WHEN e.Status='ACTIVE' THEN e.UsedQuantity ELSE 0 END) UsedQuantity
FROM dbo.PartnerSubscriptions s JOIN dbo.SubscriptionEntitlements e ON e.PartnerSubscriptionId=s.Id JOIN dbo.Services sv ON sv.Id=e.ServiceId WHERE s.Status='ACTIVE' GROUP BY s.PartnerId,sv.Code,sv.Name;
GO
PRINT '022-subscription-billing-lifecycle.sql: OK';
GO