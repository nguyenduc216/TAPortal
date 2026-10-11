/* TAPortal 020 - Credit wallet transactional procedures and immutable ledger enforcement. */
USE [TAPortal];
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.CreditLedger') AND name='IX_CreditLedger_PartnerTypeCreated')
 CREATE INDEX IX_CreditLedger_PartnerTypeCreated ON dbo.CreditLedger(PartnerId,CreditType,CreatedAt DESC) INCLUDE(EntryType,Quantity,BalanceAfter,SourceType,SourceId);
GO
IF OBJECT_ID(N'dbo.sp_GrantCredit',N'P') IS NULL EXEC('CREATE PROCEDURE dbo.sp_GrantCredit AS BEGIN SET NOCOUNT ON; END');
GO
ALTER PROCEDURE dbo.sp_GrantCredit
 @PartnerId uniqueidentifier,@CreditType varchar(20),@Quantity decimal(18,2),@SourceType varchar(50)=NULL,@SourceId nvarchar(200)=NULL,@IdempotencyKey nvarchar(200)=NULL,@Description nvarchar(500)=NULL
AS
BEGIN
 SET NOCOUNT ON;SET XACT_ABORT ON;
 IF @Quantity<=0 THROW 51101,'Quantity must be positive.',1;
 BEGIN TRAN;
 IF @IdempotencyKey IS NOT NULL AND EXISTS(SELECT 1 FROM dbo.CreditLedger WITH(UPDLOCK,HOLDLOCK) WHERE PartnerId=@PartnerId AND IdempotencyKey=@IdempotencyKey) BEGIN COMMIT; RETURN; END;
 IF NOT EXISTS(SELECT 1 FROM dbo.PartnerCreditWallets WITH(UPDLOCK,HOLDLOCK) WHERE PartnerId=@PartnerId AND CreditType=@CreditType)
  INSERT dbo.PartnerCreditWallets(PartnerId,CreditType) VALUES(@PartnerId,@CreditType);
 DECLARE @balance decimal(18,2);
 UPDATE dbo.PartnerCreditWallets SET CurrentBalance=CurrentBalance+@Quantity WHERE PartnerId=@PartnerId AND CreditType=@CreditType;
 SELECT @balance=CurrentBalance FROM dbo.PartnerCreditWallets WHERE PartnerId=@PartnerId AND CreditType=@CreditType;
 INSERT dbo.CreditLedger(PartnerId,CreditType,EntryType,Quantity,BalanceAfter,SourceType,SourceId,IdempotencyKey,Description) VALUES(@PartnerId,@CreditType,'GRANT',@Quantity,@balance,@SourceType,@SourceId,@IdempotencyKey,@Description);
 COMMIT;
END
GO
IF OBJECT_ID(N'dbo.sp_ReserveCredit',N'P') IS NULL EXEC('CREATE PROCEDURE dbo.sp_ReserveCredit AS BEGIN SET NOCOUNT ON; END');
GO
ALTER PROCEDURE dbo.sp_ReserveCredit
 @PartnerId uniqueidentifier,@CreditType varchar(20),@Quantity decimal(18,2),@PurposeType varchar(50),@PurposeId nvarchar(200),@ExpiresAt datetime2(3)=NULL
AS
BEGIN
 SET NOCOUNT ON;SET XACT_ABORT ON;
 IF @Quantity<=0 THROW 51111,'Quantity must be positive.',1;
 BEGIN TRAN;
 IF EXISTS(SELECT 1 FROM dbo.CreditReservations WITH(UPDLOCK,HOLDLOCK) WHERE PartnerId=@PartnerId AND CreditType=@CreditType AND PurposeType=@PurposeType AND PurposeId=@PurposeId AND Status='ACTIVE') BEGIN COMMIT; RETURN; END;
 DECLARE @current decimal(18,2),@reserved decimal(18,2);
 SELECT @current=CurrentBalance,@reserved=ReservedBalance FROM dbo.PartnerCreditWallets WITH(UPDLOCK,HOLDLOCK) WHERE PartnerId=@PartnerId AND CreditType=@CreditType;
 IF @current IS NULL THROW 51112,'Credit wallet not found.',1;
 IF @current-@reserved<@Quantity THROW 51113,'Insufficient available credit.',1;
 INSERT dbo.CreditReservations(PartnerId,CreditType,Quantity,PurposeType,PurposeId,ExpiresAt) VALUES(@PartnerId,@CreditType,@Quantity,@PurposeType,@PurposeId,@ExpiresAt);
 UPDATE dbo.PartnerCreditWallets SET ReservedBalance=ReservedBalance+@Quantity WHERE PartnerId=@PartnerId AND CreditType=@CreditType;
 COMMIT;
END
GO
IF OBJECT_ID(N'dbo.sp_ConsumeReservedCredit',N'P') IS NULL EXEC('CREATE PROCEDURE dbo.sp_ConsumeReservedCredit AS BEGIN SET NOCOUNT ON; END');
GO
ALTER PROCEDURE dbo.sp_ConsumeReservedCredit
 @ReservationId uniqueidentifier,@IdempotencyKey nvarchar(200)
AS
BEGIN
 SET NOCOUNT ON;SET XACT_ABORT ON;
 BEGIN TRAN;
 DECLARE @partner uniqueidentifier,@type varchar(20),@qty decimal(18,2),@purposeType varchar(50),@purposeId nvarchar(200),@balance decimal(18,2);
 SELECT @partner=PartnerId,@type=CreditType,@qty=Quantity,@purposeType=PurposeType,@purposeId=PurposeId FROM dbo.CreditReservations WITH(UPDLOCK,HOLDLOCK) WHERE Id=@ReservationId AND Status='ACTIVE';
 IF @partner IS NULL BEGIN IF EXISTS(SELECT 1 FROM dbo.CreditLedger WHERE IdempotencyKey=@IdempotencyKey) BEGIN COMMIT;RETURN;END; THROW 51121,'Active reservation not found.',1; END;
 UPDATE dbo.PartnerCreditWallets SET CurrentBalance=CurrentBalance-@qty,ReservedBalance=ReservedBalance-@qty WHERE PartnerId=@partner AND CreditType=@type AND CurrentBalance>=@qty AND ReservedBalance>=@qty;
 IF @@ROWCOUNT=0 THROW 51122,'Wallet balance invariant failed.',1;
 SELECT @balance=CurrentBalance FROM dbo.PartnerCreditWallets WHERE PartnerId=@partner AND CreditType=@type;
 INSERT dbo.CreditLedger(PartnerId,CreditType,EntryType,Quantity,BalanceAfter,SourceType,SourceId,IdempotencyKey,Description) VALUES(@partner,@type,'CONSUME',-@qty,@balance,@purposeType,@purposeId,@IdempotencyKey,N'Consume reserved credit');
 UPDATE dbo.CreditReservations SET Status='CONSUMED',ConsumedAt=SYSUTCDATETIME() WHERE Id=@ReservationId;
 COMMIT;
END
GO
IF OBJECT_ID(N'dbo.sp_ReleaseCreditReservation',N'P') IS NULL EXEC('CREATE PROCEDURE dbo.sp_ReleaseCreditReservation AS BEGIN SET NOCOUNT ON; END');
GO
ALTER PROCEDURE dbo.sp_ReleaseCreditReservation @ReservationId uniqueidentifier
AS
BEGIN
 SET NOCOUNT ON;SET XACT_ABORT ON;BEGIN TRAN;
 DECLARE @partner uniqueidentifier,@type varchar(20),@qty decimal(18,2);
 SELECT @partner=PartnerId,@type=CreditType,@qty=Quantity FROM dbo.CreditReservations WITH(UPDLOCK,HOLDLOCK) WHERE Id=@ReservationId AND Status='ACTIVE';
 IF @partner IS NULL BEGIN COMMIT;RETURN;END;
 UPDATE dbo.PartnerCreditWallets SET ReservedBalance=CASE WHEN ReservedBalance>=@qty THEN ReservedBalance-@qty ELSE 0 END WHERE PartnerId=@partner AND CreditType=@type;
 UPDATE dbo.CreditReservations SET Status='RELEASED',ReleasedAt=SYSUTCDATETIME() WHERE Id=@ReservationId;
 COMMIT;
END
GO
IF OBJECT_ID(N'dbo.tr_CreditLedger_NoUpdateDelete',N'TR') IS NULL EXEC('CREATE TRIGGER dbo.tr_CreditLedger_NoUpdateDelete ON dbo.CreditLedger INSTEAD OF UPDATE,DELETE AS BEGIN THROW 51190,''CreditLedger is immutable; use compensating ledger entries.'',1; END');
GO
PRINT '020-credit-wallet-procedures.sql: OK';
GO