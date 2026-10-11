/* V1 RC3 042 - customer money + product credit cut-over. */
USE [TAPortal];
GO
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO
IF OBJECT_ID(N'dbo.tr_CustomerCashLedger_NoUpdateDelete',N'TR') IS NULL EXEC('CREATE TRIGGER dbo.tr_CustomerCashLedger_NoUpdateDelete ON dbo.CustomerCashLedger AFTER UPDATE,DELETE AS BEGIN SET NOCOUNT ON; THROW 51420,''CustomerCashLedger is immutable; post compensating entries.'',1; END');
GO
IF OBJECT_ID(N'dbo.sp_PostCustomerCash',N'P') IS NULL EXEC('CREATE PROCEDURE dbo.sp_PostCustomerCash AS BEGIN SET NOCOUNT ON; END');
GO
ALTER PROCEDURE dbo.sp_PostCustomerCash @AccountId uniqueidentifier,@EntryType varchar(20),@Amount decimal(18,2),@PaymentTransactionId uniqueidentifier=NULL,@Reason nvarchar(500)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON; BEGIN TRAN;
 DECLARE @old decimal(18,2),@new decimal(18,2);
 SELECT @old=Balance FROM dbo.CustomerCashAccounts WITH(UPDLOCK,HOLDLOCK) WHERE Id=@AccountId;
 IF @old IS NULL THROW 51421,'Customer cash account not found.',1;
 SET @new=@old+@Amount; IF @new<0 THROW 51422,'Customer cash balance cannot be negative.',1;
 UPDATE dbo.CustomerCashAccounts SET Balance=@new WHERE Id=@AccountId;
 INSERT dbo.CustomerCashLedger(CustomerCashAccountId,EntryType,Amount,BalanceAfter,PaymentTransactionId,Reason) VALUES(@AccountId,@EntryType,@Amount,@new,@PaymentTransactionId,@Reason);
 COMMIT;
END
GO
IF OBJECT_ID(N'dbo.sp_GrantSubscriptionCredit',N'P') IS NULL EXEC('CREATE PROCEDURE dbo.sp_GrantSubscriptionCredit AS BEGIN SET NOCOUNT ON; END');
GO
ALTER PROCEDURE dbo.sp_GrantSubscriptionCredit @GrantId uniqueidentifier
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON; BEGIN TRAN;
 DECLARE @partner uniqueidentifier,@def uniqueidentifier,@qty decimal(18,2),@idem nvarchar(200),@wallet uniqueidentifier,@ctype varchar(20),@bal decimal(18,2),@ledger bigint;
 SELECT @partner=PartnerId,@def=CreditDefinitionId,@qty=Quantity,@idem=IdempotencyKey FROM dbo.SubscriptionCreditGrants WITH(UPDLOCK,HOLDLOCK) WHERE Id=@GrantId AND Status='PENDING';
 IF @partner IS NULL BEGIN COMMIT; RETURN; END;
 SELECT @ctype=CASE Code WHEN 'BANK_TRANSACTION_UNIT' THEN 'BANKING' WHEN 'INVOICE_ISSUE_UNIT' THEN 'EINVOICE' END FROM dbo.CreditDefinitions WHERE Id=@def;
 SELECT @wallet=Id,@bal=CurrentBalance FROM dbo.PartnerCreditWallets WITH(UPDLOCK,HOLDLOCK) WHERE PartnerId=@partner AND CreditDefinitionId=@def;
 IF @wallet IS NULL THROW 51423,'Credit wallet not found.',1;
 IF EXISTS(SELECT 1 FROM dbo.CreditLedger WHERE PartnerId=@partner AND IdempotencyKey=@idem) THROW 51424,'Duplicate credit grant idempotency key.',1;
 SET @bal=@bal+@qty;
 UPDATE dbo.PartnerCreditWallets SET CurrentBalance=@bal WHERE Id=@wallet;
 INSERT dbo.CreditLedger(PartnerId,CreditType,CreditDefinitionId,EntryType,Quantity,BalanceAfter,SourceType,SourceId,IdempotencyKey,Description) VALUES(@partner,@ctype,@def,'GRANT',@qty,@bal,'SUBSCRIPTION_GRANT',CONVERT(nvarchar(200),@GrantId),@idem,N'Subscription period credit grant');
 SET @ledger=SCOPE_IDENTITY();
 UPDATE dbo.SubscriptionCreditGrants SET Status='GRANTED',CreditLedgerId=@ledger,GrantedAt=SYSUTCDATETIME() WHERE Id=@GrantId;
 COMMIT;
END
GO
PRINT '042-money-credit-cutover.sql: OK';
GO