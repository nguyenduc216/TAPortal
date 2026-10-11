/* V1 RC4 045 - credit cut-over repair. SQL Server 2014 compatible. */
USE [TAPortal];
GO
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO
IF OBJECT_ID(N'dbo.sp_GrantSubscriptionCredit',N'P') IS NULL EXEC('CREATE PROCEDURE dbo.sp_GrantSubscriptionCredit AS BEGIN SET NOCOUNT ON; END');
GO
ALTER PROCEDURE dbo.sp_GrantSubscriptionCredit @GrantId uniqueidentifier
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRAN;
  DECLARE @partner uniqueidentifier,@def uniqueidentifier,@qty decimal(18,2),@idem nvarchar(200),@wallet uniqueidentifier,@ctype varchar(20),@bal decimal(18,2),@ledger bigint;
  SELECT @partner=PartnerId,@def=CreditDefinitionId,@qty=Quantity,@idem=IdempotencyKey FROM dbo.SubscriptionCreditGrants WITH(UPDLOCK,HOLDLOCK) WHERE Id=@GrantId AND Status='PENDING';
  IF @partner IS NULL BEGIN COMMIT; RETURN; END;
  SELECT @ctype=CASE Code WHEN 'BANK_TRANSACTION_UNIT' THEN 'BANKING' WHEN 'INVOICE_ISSUE_UNIT' THEN 'EINVOICE' END FROM dbo.CreditDefinitions WHERE Id=@def;
  IF @ctype IS NULL THROW 51601,'Unsupported credit definition.',1;
  SELECT @wallet=Id,@bal=CurrentBalance FROM dbo.PartnerCreditWallets WITH(UPDLOCK,HOLDLOCK) WHERE PartnerId=@partner AND CreditDefinitionId=@def;
  IF @wallet IS NULL THROW 51602,'Credit wallet not found.',1;
  IF EXISTS(SELECT 1 FROM dbo.CreditLedger WHERE PartnerId=@partner AND IdempotencyKey=@idem) BEGIN
    SELECT @ledger=Id FROM dbo.CreditLedger WHERE PartnerId=@partner AND IdempotencyKey=@idem;
    UPDATE dbo.SubscriptionCreditGrants SET Status='GRANTED',CreditLedgerId=@ledger,GrantedAt=COALESCE(GrantedAt,SYSUTCDATETIME()) WHERE Id=@GrantId;
    COMMIT; RETURN;
  END;
  SET @bal=@bal+@qty;
  UPDATE dbo.PartnerCreditWallets SET CurrentBalance=@bal WHERE Id=@wallet;
  INSERT dbo.CreditLedger(PartnerId,CreditType,CreditDefinitionId,EntryType,Quantity,BalanceAfter,SourceType,SourceId,IdempotencyKey,Description) VALUES(@partner,@ctype,@def,'GRANT',@qty,@bal,'SUBSCRIPTION_GRANT',CONVERT(nvarchar(200),@GrantId),@idem,N'Subscription period credit grant');
  SET @ledger=SCOPE_IDENTITY();
  UPDATE dbo.SubscriptionCreditGrants SET Status='GRANTED',CreditLedgerId=@ledger,GrantedAt=SYSUTCDATETIME() WHERE Id=@GrantId;
  COMMIT;
 END TRY BEGIN CATCH IF XACT_STATE()<>0 ROLLBACK; THROW; END CATCH
END
GO
IF EXISTS(SELECT 1 FROM dbo.PartnerCreditWallets WHERE CreditDefinitionId IS NULL) THROW 51603,'Wallet CreditDefinitionId cut-over incomplete.',1;
IF EXISTS(SELECT 1 FROM dbo.CreditReservations WHERE CreditDefinitionId IS NULL) THROW 51604,'Reservation CreditDefinitionId cut-over incomplete.',1;
IF EXISTS(SELECT 1 FROM dbo.CreditUsageRules WHERE CreditDefinitionId IS NULL) THROW 51605,'Usage rule CreditDefinitionId cut-over incomplete.',1;
GO
PRINT '045-credit-cutover-repair.sql: OK';
GO