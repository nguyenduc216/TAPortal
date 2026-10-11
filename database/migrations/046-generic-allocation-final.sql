/* V1 RC4 046 - generic allocation final cut-over. */
USE [TAPortal];
GO
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO
IF EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.PaymentAllocations') AND name='UX_PA_RequestTransactionActive') DROP INDEX UX_PA_RequestTransactionActive ON dbo.PaymentAllocations;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.PaymentAllocations') AND name='UX_PA_RequestPaymentTransactionActive') CREATE UNIQUE INDEX UX_PA_RequestPaymentTransactionActive ON dbo.PaymentAllocations(PaymentRequestId,PaymentTransactionId) WHERE Status='ACTIVE';
IF COL_LENGTH('dbo.PaymentAllocations','OperationKey') IS NULL ALTER TABLE dbo.PaymentAllocations ADD OperationKey nvarchar(200) NULL;
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.PaymentAllocations') AND name='UX_PA_PartnerOperation') CREATE UNIQUE INDEX UX_PA_PartnerOperation ON dbo.PaymentAllocations(PartnerId,OperationKey) WHERE OperationKey IS NOT NULL;
GO
ALTER PROCEDURE dbo.sp_AllocatePaymentTransaction
 @PartnerId uniqueidentifier,@PaymentRequestId uniqueidentifier,@PaymentTransactionId uniqueidentifier,@AllocatedAmount decimal(18,2)=NULL,@OperationKey nvarchar(200)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRAN;
  IF @OperationKey IS NOT NULL AND EXISTS(SELECT 1 FROM dbo.PaymentAllocations WITH(UPDLOCK,HOLDLOCK) WHERE PartnerId=@PartnerId AND OperationKey=@OperationKey) BEGIN COMMIT; RETURN; END;
  DECLARE @due decimal(18,2),@tx decimal(18,2),@reqPaid decimal(18,2),@txUsed decimal(18,2),@amt decimal(18,2),@status varchar(20);
  SELECT @due=AmountDue,@status=Status FROM dbo.PaymentRequests WITH(UPDLOCK,HOLDLOCK) WHERE Id=@PaymentRequestId AND PartnerId=@PartnerId;
  IF @due IS NULL THROW 51610,'Payment request not found for partner.',1;
  IF @status IN('EXPIRED','CANCELLED') THROW 51611,'Payment request is not allocatable.',1;
  SELECT @tx=Amount FROM dbo.PaymentTransactions WITH(UPDLOCK,HOLDLOCK) WHERE Id=@PaymentTransactionId AND PartnerId=@PartnerId AND Direction='CREDIT';
  IF @tx IS NULL THROW 51612,'Credit payment transaction not found for partner.',1;
  SELECT @reqPaid=ISNULL(SUM(AllocatedAmount),0) FROM dbo.PaymentAllocations WITH(UPDLOCK,HOLDLOCK) WHERE PaymentRequestId=@PaymentRequestId AND Status='ACTIVE';
  SELECT @txUsed=ISNULL(SUM(AllocatedAmount),0) FROM dbo.PaymentAllocations WITH(UPDLOCK,HOLDLOCK) WHERE PaymentTransactionId=@PaymentTransactionId AND Status='ACTIVE';
  SET @amt=COALESCE(@AllocatedAmount,CASE WHEN @tx-@txUsed<@due-@reqPaid THEN @tx-@txUsed ELSE @due-@reqPaid END);
  IF @amt<=0 OR @amt>@tx-@txUsed OR @amt>@due-@reqPaid THROW 51613,'Allocation exceeds remaining transaction or receivable amount.',1;
  INSERT dbo.PaymentAllocations(Id,PartnerId,PaymentRequestId,PaymentTransactionId,BankTransactionId,AllocatedAmount,Status,AllocatedAt,OperationKey) VALUES(NEWID(),@PartnerId,@PaymentRequestId,@PaymentTransactionId,NULL,@amt,'ACTIVE',SYSUTCDATETIME(),@OperationKey);
  SET @reqPaid=@reqPaid+@amt;
  UPDATE dbo.PaymentRequests SET AmountPaid=@reqPaid,Status=CASE WHEN @reqPaid=0 THEN 'PENDING' WHEN @reqPaid<@due THEN 'PARTIALLY_PAID' ELSE 'PAID' END,UpdatedAt=SYSUTCDATETIME() WHERE Id=@PaymentRequestId;
  COMMIT;
 END TRY BEGIN CATCH IF XACT_STATE()<>0 ROLLBACK; THROW; END CATCH
END
GO
ALTER PROCEDURE dbo.sp_ReversePaymentAllocationV2 @PartnerId uniqueidentifier,@AllocationId uniqueidentifier,@Reason nvarchar(500)
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY BEGIN TRAN;
  DECLARE @r uniqueidentifier,@due decimal(18,2),@paid decimal(18,2);
  SELECT @r=PaymentRequestId FROM dbo.PaymentAllocations WITH(UPDLOCK,HOLDLOCK) WHERE Id=@AllocationId AND PartnerId=@PartnerId AND Status='ACTIVE';
  IF @r IS NULL THROW 51614,'Active allocation not found.',1;
  UPDATE dbo.PaymentAllocations SET Status='REVERSED',ReversedAt=SYSUTCDATETIME(),ReversalReason=@Reason WHERE Id=@AllocationId;
  SELECT @due=AmountDue FROM dbo.PaymentRequests WITH(UPDLOCK,HOLDLOCK) WHERE Id=@r AND PartnerId=@PartnerId;
  SELECT @paid=ISNULL(SUM(AllocatedAmount),0) FROM dbo.PaymentAllocations WHERE PaymentRequestId=@r AND Status='ACTIVE';
  UPDATE dbo.PaymentRequests SET AmountPaid=@paid,Status=CASE WHEN @paid=0 THEN 'PENDING' WHEN @paid<@due THEN 'PARTIALLY_PAID' ELSE 'PAID' END,UpdatedAt=SYSUTCDATETIME() WHERE Id=@r;
  COMMIT;
 END TRY BEGIN CATCH IF XACT_STATE()<>0 ROLLBACK; THROW; END CATCH
END
GO
IF OBJECT_ID(N'dbo.sp_AllocateBankTransaction',N'P') IS NOT NULL
BEGIN
 EXEC('ALTER PROCEDURE dbo.sp_AllocateBankTransaction AS BEGIN SET NOCOUNT ON; THROW 51615,''Legacy bank allocation API disabled; use sp_AllocatePaymentTransaction.'',1; END');
END
GO
PRINT '046-generic-allocation-final.sql: OK';
GO