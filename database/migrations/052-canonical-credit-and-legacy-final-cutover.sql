/* V1 RC6 052 - canonical credit and legacy final cut-over. SQL Server 2014 compatible. */
USE [TAPortal];
GO
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

UPDATE w SET CreditDefinitionId=d.Id FROM dbo.PartnerCreditWallets w JOIN dbo.CreditDefinitions d ON d.Code=CASE w.CreditType WHEN 'BANKING' THEN 'BANK_TRANSACTION_UNIT' WHEN 'EINVOICE' THEN 'INVOICE_ISSUE_UNIT' END WHERE w.CreditDefinitionId IS NULL;
UPDATE r SET CreditDefinitionId=d.Id FROM dbo.CreditReservations r JOIN dbo.CreditDefinitions d ON d.Code=CASE r.CreditType WHEN 'BANKING' THEN 'BANK_TRANSACTION_UNIT' WHEN 'EINVOICE' THEN 'INVOICE_ISSUE_UNIT' END WHERE r.CreditDefinitionId IS NULL;
UPDATE r SET CreditDefinitionId=d.Id FROM dbo.CreditUsageRules r JOIN dbo.CreditDefinitions d ON d.Code=CASE r.CreditType WHEN 'BANKING' THEN 'BANK_TRANSACTION_UNIT' WHEN 'EINVOICE' THEN 'INVOICE_ISSUE_UNIT' END WHERE r.CreditDefinitionId IS NULL;
UPDATE t SET CreditDefinitionId=d.Id FROM dbo.CreditTopups t JOIN dbo.CreditDefinitions d ON d.Code=CASE t.CreditType WHEN 'BANKING' THEN 'BANK_TRANSACTION_UNIT' WHEN 'EINVOICE' THEN 'INVOICE_ISSUE_UNIT' END WHERE t.CreditDefinitionId IS NULL;
GO
BEGIN TRY
 BEGIN TRAN;
 DISABLE TRIGGER dbo.tr_CreditLedger_NoUpdateDelete ON dbo.CreditLedger;
 UPDATE l SET CreditDefinitionId=d.Id FROM dbo.CreditLedger l JOIN dbo.CreditDefinitions d ON d.Code=CASE l.CreditType WHEN 'BANKING' THEN 'BANK_TRANSACTION_UNIT' WHEN 'EINVOICE' THEN 'INVOICE_ISSUE_UNIT' END WHERE l.CreditDefinitionId IS NULL;
 ENABLE TRIGGER dbo.tr_CreditLedger_NoUpdateDelete ON dbo.CreditLedger;
 COMMIT;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK;
 ENABLE TRIGGER dbo.tr_CreditLedger_NoUpdateDelete ON dbo.CreditLedger;
 THROW;
END CATCH
GO
IF EXISTS(SELECT 1 FROM dbo.PartnerCreditWallets WHERE CreditDefinitionId IS NULL) THROW 52001,'Wallet credit definition backfill incomplete.',1;
IF EXISTS(SELECT 1 FROM dbo.CreditReservations WHERE CreditDefinitionId IS NULL) THROW 52002,'Reservation credit definition backfill incomplete.',1;
IF EXISTS(SELECT 1 FROM dbo.CreditUsageRules WHERE CreditDefinitionId IS NULL) THROW 52003,'Usage rule credit definition backfill incomplete.',1;
IF EXISTS(SELECT 1 FROM dbo.CreditTopups WHERE CreditDefinitionId IS NULL) THROW 52004,'Topup credit definition backfill incomplete.',1;
IF EXISTS(SELECT 1 FROM dbo.CreditLedger WHERE CreditDefinitionId IS NULL) THROW 52005,'Ledger credit definition backfill incomplete.',1;
GO
/* UX_Wallet_Definition was created in 035 on the nullable column. SQL Server 2014
   requires the dependent index to be removed before changing nullability. */
IF EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.PartnerCreditWallets') AND name='UX_Wallet_Definition')
 DROP INDEX UX_Wallet_Definition ON dbo.PartnerCreditWallets;
GO
ALTER TABLE dbo.PartnerCreditWallets ALTER COLUMN CreditDefinitionId uniqueidentifier NOT NULL;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.PartnerCreditWallets') AND name='UX_Wallet_Definition')
 CREATE UNIQUE INDEX UX_Wallet_Definition ON dbo.PartnerCreditWallets(PartnerId,CreditDefinitionId);
GO
ALTER TABLE dbo.CreditReservations ALTER COLUMN CreditDefinitionId uniqueidentifier NOT NULL;
ALTER TABLE dbo.CreditUsageRules ALTER COLUMN CreditDefinitionId uniqueidentifier NOT NULL;
ALTER TABLE dbo.CreditTopups ALTER COLUMN CreditDefinitionId uniqueidentifier NOT NULL;
ALTER TABLE dbo.CreditLedger ALTER COLUMN CreditDefinitionId uniqueidentifier NOT NULL;
GO
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name='FK_CR_CreditDefinition') ALTER TABLE dbo.CreditReservations ADD CONSTRAINT FK_CR_CreditDefinition FOREIGN KEY(CreditDefinitionId) REFERENCES dbo.CreditDefinitions(Id);
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name='FK_CUR_CreditDefinition') ALTER TABLE dbo.CreditUsageRules ADD CONSTRAINT FK_CUR_CreditDefinition FOREIGN KEY(CreditDefinitionId) REFERENCES dbo.CreditDefinitions(Id);
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name='FK_CT_CreditDefinition') ALTER TABLE dbo.CreditTopups ADD CONSTRAINT FK_CT_CreditDefinition FOREIGN KEY(CreditDefinitionId) REFERENCES dbo.CreditDefinitions(Id);
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name='FK_CL_CreditDefinition') ALTER TABLE dbo.CreditLedger ADD CONSTRAINT FK_CL_CreditDefinition FOREIGN KEY(CreditDefinitionId) REFERENCES dbo.CreditDefinitions(Id);
GO

ALTER PROCEDURE dbo.sp_GrantCredit
 @PartnerId uniqueidentifier,@CreditType varchar(20),@Quantity decimal(18,2),@SourceType varchar(50)=NULL,@SourceId nvarchar(200)=NULL,@IdempotencyKey nvarchar(200)=NULL,@Description nvarchar(500)=NULL
AS
BEGIN
 SET NOCOUNT ON;
 THROW 52010,'Legacy type-only grant is disabled; use canonical CreditDefinition grant flow.',1;
END
GO
ALTER PROCEDURE dbo.sp_ReserveCredit
 @PartnerId uniqueidentifier,@CreditType varchar(20),@Quantity decimal(18,2),@PurposeType varchar(50),@PurposeId nvarchar(200),@ExpiresAt datetime2(3)=NULL
AS
BEGIN
 SET NOCOUNT ON;
 THROW 52011,'Legacy type-only reserve is disabled; use canonical CreditDefinition reservation flow.',1;
END
GO

IF OBJECT_ID(N'dbo.sp_ReserveCreditDefinition',N'P') IS NULL EXEC('CREATE PROCEDURE dbo.sp_ReserveCreditDefinition AS BEGIN SET NOCOUNT ON; END');
GO
ALTER PROCEDURE dbo.sp_ReserveCreditDefinition
 @PartnerId uniqueidentifier,@CreditDefinitionId uniqueidentifier,@Quantity decimal(18,2),@PurposeType varchar(50),@PurposeId nvarchar(200),@ExpiresAt datetime2(3)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF @Quantity<=0 THROW 52012,'Quantity must be positive.',1;
 BEGIN TRY
  BEGIN TRAN;
  DECLARE @wallet uniqueidentifier,@type varchar(20),@current decimal(18,2),@reserved decimal(18,2);
  SELECT @type=CASE Code WHEN 'BANK_TRANSACTION_UNIT' THEN 'BANKING' WHEN 'INVOICE_ISSUE_UNIT' THEN 'EINVOICE' END FROM dbo.CreditDefinitions WHERE Id=@CreditDefinitionId AND IsActive=1;
  IF @type IS NULL THROW 52013,'Unsupported or inactive credit definition.',1;
  IF EXISTS(SELECT 1 FROM dbo.CreditReservations WITH(UPDLOCK,HOLDLOCK) WHERE PartnerId=@PartnerId AND CreditDefinitionId=@CreditDefinitionId AND PurposeType=@PurposeType AND PurposeId=@PurposeId AND Status='ACTIVE') BEGIN COMMIT; RETURN; END;
  SELECT @wallet=Id,@current=CurrentBalance,@reserved=ReservedBalance FROM dbo.PartnerCreditWallets WITH(UPDLOCK,HOLDLOCK) WHERE PartnerId=@PartnerId AND CreditDefinitionId=@CreditDefinitionId;
  IF @wallet IS NULL THROW 52014,'Canonical credit wallet not found.',1;
  IF @current-@reserved<@Quantity THROW 52015,'Insufficient available credit.',1;
  INSERT dbo.CreditReservations(PartnerId,CreditType,CreditDefinitionId,Quantity,PurposeType,PurposeId,ExpiresAt) VALUES(@PartnerId,@type,@CreditDefinitionId,@Quantity,@PurposeType,@PurposeId,@ExpiresAt);
  UPDATE dbo.PartnerCreditWallets SET ReservedBalance=ReservedBalance+@Quantity WHERE Id=@wallet;
  COMMIT;
 END TRY BEGIN CATCH IF XACT_STATE()<>0 ROLLBACK; THROW; END CATCH
END
GO

IF OBJECT_ID(N'dbo.tr_PaymentRequests_LegacyQrReadOnly',N'TR') IS NULL EXEC('CREATE TRIGGER dbo.tr_PaymentRequests_LegacyQrReadOnly ON dbo.PaymentRequests AFTER INSERT,UPDATE AS BEGIN SET NOCOUNT ON; END');
GO
ALTER TRIGGER dbo.tr_PaymentRequests_LegacyQrReadOnly ON dbo.PaymentRequests AFTER INSERT,UPDATE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.Id=i.Id WHERE (d.Id IS NULL AND (i.QrProvider IS NOT NULL OR i.QrPayload IS NOT NULL OR i.QrGeneratedAt IS NOT NULL)) OR (d.Id IS NOT NULL AND (ISNULL(i.QrProvider,'')<>ISNULL(d.QrProvider,'') OR ISNULL(i.QrPayload,'')<>ISNULL(d.QrPayload,'') OR ISNULL(CONVERT(varchar(33),i.QrGeneratedAt,126),'')<>ISNULL(CONVERT(varchar(33),d.QrGeneratedAt,126),'')))) THROW 52020,'Legacy PaymentRequest QR fields are read-only; use PaymentInstructions.',1;
END
GO

IF EXISTS(SELECT 1 FROM sys.default_constraints WHERE parent_object_id=OBJECT_ID('dbo.PartnerUsers') AND name='DF_PartnerUsers_Role') ALTER TABLE dbo.PartnerUsers DROP CONSTRAINT DF_PartnerUsers_Role;
IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('dbo.PartnerUsers') AND name='CK_PartnerUsers_Role') ALTER TABLE dbo.PartnerUsers DROP CONSTRAINT CK_PartnerUsers_Role;
ALTER TABLE dbo.PartnerUsers ALTER COLUMN PartnerRole varchar(30) NULL;
GO
IF OBJECT_ID(N'dbo.tr_PartnerUsers_LegacyRoleReadOnly',N'TR') IS NULL EXEC('CREATE TRIGGER dbo.tr_PartnerUsers_LegacyRoleReadOnly ON dbo.PartnerUsers AFTER INSERT,UPDATE AS BEGIN SET NOCOUNT ON; END');
GO
ALTER TRIGGER dbo.tr_PartnerUsers_LegacyRoleReadOnly ON dbo.PartnerUsers AFTER INSERT,UPDATE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.PartnerId=i.PartnerId AND d.UserId=i.UserId WHERE (d.UserId IS NULL AND i.PartnerRole IS NOT NULL) OR (d.UserId IS NOT NULL AND ISNULL(i.PartnerRole,'')<>ISNULL(d.PartnerRole,''))) THROW 52021,'Legacy PartnerRole is read-only; use PartnerUserRoles.',1;
END
GO

ALTER PROCEDURE dbo.sp_ConsumeReservedCredit @ReservationId uniqueidentifier,@IdempotencyKey nvarchar(200)
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRAN;
  DECLARE @partner uniqueidentifier,@def uniqueidentifier,@type varchar(20),@qty decimal(18,2),@purposeType varchar(50),@purposeId nvarchar(200),@balance decimal(18,2);
  SELECT @partner=PartnerId,@def=CreditDefinitionId,@type=CreditType,@qty=Quantity,@purposeType=PurposeType,@purposeId=PurposeId FROM dbo.CreditReservations WITH(UPDLOCK,HOLDLOCK) WHERE Id=@ReservationId AND Status='ACTIVE';
  IF @partner IS NULL BEGIN IF EXISTS(SELECT 1 FROM dbo.CreditLedger WHERE IdempotencyKey=@IdempotencyKey) BEGIN COMMIT; RETURN; END; THROW 52016,'Active reservation not found.',1; END;
  UPDATE dbo.PartnerCreditWallets SET CurrentBalance=CurrentBalance-@qty,ReservedBalance=ReservedBalance-@qty WHERE PartnerId=@partner AND CreditDefinitionId=@def AND CurrentBalance>=@qty AND ReservedBalance>=@qty;
  IF @@ROWCOUNT=0 THROW 52017,'Canonical wallet balance invariant failed.',1;
  SELECT @balance=CurrentBalance FROM dbo.PartnerCreditWallets WHERE PartnerId=@partner AND CreditDefinitionId=@def;
  INSERT dbo.CreditLedger(PartnerId,CreditType,CreditDefinitionId,EntryType,Quantity,BalanceAfter,SourceType,SourceId,IdempotencyKey,Description) VALUES(@partner,@type,@def,'CONSUME',-@qty,@balance,@purposeType,@purposeId,@IdempotencyKey,N'Consume reserved credit');
  UPDATE dbo.CreditReservations SET Status='CONSUMED',ConsumedAt=SYSUTCDATETIME() WHERE Id=@ReservationId;
  COMMIT;
 END TRY BEGIN CATCH IF XACT_STATE()<>0 ROLLBACK; THROW; END CATCH
END
GO
ALTER PROCEDURE dbo.sp_ReleaseCreditReservation @ReservationId uniqueidentifier
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRAN;
  DECLARE @partner uniqueidentifier,@def uniqueidentifier,@qty decimal(18,2);
  SELECT @partner=PartnerId,@def=CreditDefinitionId,@qty=Quantity FROM dbo.CreditReservations WITH(UPDLOCK,HOLDLOCK) WHERE Id=@ReservationId AND Status='ACTIVE';
  IF @partner IS NULL BEGIN COMMIT; RETURN; END;
  UPDATE dbo.PartnerCreditWallets SET ReservedBalance=CASE WHEN ReservedBalance>=@qty THEN ReservedBalance-@qty ELSE 0 END WHERE PartnerId=@partner AND CreditDefinitionId=@def;
  IF @@ROWCOUNT=0 THROW 52018,'Canonical credit wallet not found for reservation.',1;
  UPDATE dbo.CreditReservations SET Status='RELEASED',ReleasedAt=SYSUTCDATETIME() WHERE Id=@ReservationId;
  COMMIT;
 END TRY BEGIN CATCH IF XACT_STATE()<>0 ROLLBACK; THROW; END CATCH
END
GO
PRINT '052-canonical-credit-and-legacy-final-cutover.sql: OK';
GO