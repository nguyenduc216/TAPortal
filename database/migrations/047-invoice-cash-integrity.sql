/* V1 RC4 047 - invoice item immutability, real credit consumption, customer cash integrity. */
USE [TAPortal];
GO
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO
IF OBJECT_ID(N'dbo.tr_InvoiceItems_FrozenSnapshot',N'TR') IS NULL EXEC('CREATE TRIGGER dbo.tr_InvoiceItems_FrozenSnapshot ON dbo.InvoiceItems AFTER INSERT,UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM inserted x JOIN dbo.InvoiceRequests r ON r.Id=x.InvoiceRequestId WHERE r.SnapshotFrozenAt IS NOT NULL) OR EXISTS(SELECT 1 FROM deleted x JOIN dbo.InvoiceRequests r ON r.Id=x.InvoiceRequestId WHERE r.SnapshotFrozenAt IS NOT NULL) THROW 51620,''Frozen invoice items cannot be modified.'',1; END');
GO
ALTER PROCEDURE dbo.sp_FinalizeIssuedInvoiceCredit @PartnerId uniqueidentifier,@InvoiceRequestId uniqueidentifier,@InvoiceId uniqueidentifier
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY BEGIN TRAN;
  DECLARE @c uniqueidentifier,@status varchar(20),@reservation uniqueidentifier,@idem nvarchar(200);
  SELECT @c=Id,@status=Status,@reservation=CreditReservationId,@idem=IdempotencyKey FROM dbo.InvoiceCreditConsumptions WITH(UPDLOCK,HOLDLOCK) WHERE PartnerId=@PartnerId AND InvoiceRequestId=@InvoiceRequestId;
  IF @c IS NULL THROW 51621,'Invoice credit reservation binding not found.',1;
  IF @status='CONSUMED' BEGIN COMMIT; RETURN; END;
  IF @status<>'RESERVED' THROW 51622,'Invoice credit is not reserved.',1;
  EXEC dbo.sp_ConsumeReservedCredit @ReservationId=@reservation,@IdempotencyKey=@idem;
  UPDATE dbo.InvoiceCreditConsumptions SET InvoiceId=@InvoiceId,Status='CONSUMED',ConsumedAt=SYSUTCDATETIME() WHERE Id=@c;
  UPDATE dbo.InvoiceRequests SET Status='ISSUED' WHERE Id=@InvoiceRequestId AND PartnerId=@PartnerId;
  COMMIT;
 END TRY BEGIN CATCH IF XACT_STATE()<>0 ROLLBACK; THROW; END CATCH
END
GO
IF COL_LENGTH('dbo.CustomerCashLedger','IdempotencyKey') IS NULL ALTER TABLE dbo.CustomerCashLedger ADD IdempotencyKey nvarchar(200) NULL;
IF COL_LENGTH('dbo.CustomerCashLedger','PartnerId') IS NULL ALTER TABLE dbo.CustomerCashLedger ADD PartnerId uniqueidentifier NULL;
GO
UPDATE l SET PartnerId=a.PartnerId FROM dbo.CustomerCashLedger l JOIN dbo.CustomerCashAccounts a ON a.Id=l.CustomerCashAccountId WHERE l.PartnerId IS NULL;
ALTER TABLE dbo.CustomerCashLedger ALTER COLUMN PartnerId uniqueidentifier NOT NULL;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.CustomerCashLedger') AND name='UX_CCL_PartnerIdem') CREATE UNIQUE INDEX UX_CCL_PartnerIdem ON dbo.CustomerCashLedger(PartnerId,IdempotencyKey) WHERE IdempotencyKey IS NOT NULL;
GO
ALTER PROCEDURE dbo.sp_PostCustomerCash @PartnerId uniqueidentifier,@AccountId uniqueidentifier,@EntryType varchar(20),@Amount decimal(18,2),@PaymentTransactionId uniqueidentifier=NULL,@IdempotencyKey nvarchar(200),@Reason nvarchar(500)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY BEGIN TRAN;
  IF EXISTS(SELECT 1 FROM dbo.CustomerCashLedger WITH(UPDLOCK,HOLDLOCK) WHERE PartnerId=@PartnerId AND IdempotencyKey=@IdempotencyKey) BEGIN COMMIT; RETURN; END;
  DECLARE @old decimal(18,2),@new decimal(18,2);
  SELECT @old=Balance FROM dbo.CustomerCashAccounts WITH(UPDLOCK,HOLDLOCK) WHERE Id=@AccountId AND PartnerId=@PartnerId;
  IF @old IS NULL THROW 51623,'Customer cash account not found for partner.',1;
  IF @PaymentTransactionId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.PaymentTransactions WHERE Id=@PaymentTransactionId AND PartnerId=@PartnerId) THROW 51624,'Payment transaction tenant mismatch.',1;
  SET @new=@old+@Amount; IF @new<0 THROW 51625,'Customer cash balance cannot be negative.',1;
  UPDATE dbo.CustomerCashAccounts SET Balance=@new WHERE Id=@AccountId AND PartnerId=@PartnerId;
  INSERT dbo.CustomerCashLedger(CustomerCashAccountId,PartnerId,EntryType,Amount,BalanceAfter,PaymentTransactionId,IdempotencyKey,Reason) VALUES(@AccountId,@PartnerId,@EntryType,@Amount,@new,@PaymentTransactionId,@IdempotencyKey,@Reason);
  COMMIT;
 END TRY BEGIN CATCH IF XACT_STATE()<>0 ROLLBACK; THROW; END CATCH
END
GO
PRINT '047-invoice-cash-integrity.sql: OK';
GO