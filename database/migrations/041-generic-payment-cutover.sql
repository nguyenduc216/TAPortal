/* V1 RC3 041 - generic payment allocation cut-over. */
USE [TAPortal];
GO
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO
IF COL_LENGTH('dbo.PaymentAllocations','BankTransactionId') IS NOT NULL ALTER TABLE dbo.PaymentAllocations ALTER COLUMN BankTransactionId uniqueidentifier NULL;
GO
IF OBJECT_ID(N'dbo.sp_AllocatePaymentTransaction',N'P') IS NULL EXEC('CREATE PROCEDURE dbo.sp_AllocatePaymentTransaction AS BEGIN SET NOCOUNT ON; END');
GO
ALTER PROCEDURE dbo.sp_AllocatePaymentTransaction
 @PartnerId uniqueidentifier,@PaymentRequestId uniqueidentifier,@PaymentTransactionId uniqueidentifier,@AllocatedAmount decimal(18,2)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRAN;
 DECLARE @due decimal(18,2),@tx decimal(18,2),@reqPaid decimal(18,2),@txUsed decimal(18,2),@amt decimal(18,2),@status varchar(20);
 SELECT @due=AmountDue,@status=Status FROM dbo.PaymentRequests WITH(UPDLOCK,HOLDLOCK) WHERE Id=@PaymentRequestId AND PartnerId=@PartnerId;
 IF @due IS NULL THROW 51410,'Payment request not found for partner.',1;
 IF @status IN('EXPIRED','CANCELLED') THROW 51411,'Payment request is not allocatable.',1;
 SELECT @tx=Amount FROM dbo.PaymentTransactions WITH(UPDLOCK,HOLDLOCK) WHERE Id=@PaymentTransactionId AND PartnerId=@PartnerId AND Direction='CREDIT';
 IF @tx IS NULL THROW 51412,'Credit payment transaction not found for partner.',1;
 SELECT @reqPaid=ISNULL(SUM(AllocatedAmount),0) FROM dbo.PaymentAllocations WITH(UPDLOCK,HOLDLOCK) WHERE PaymentRequestId=@PaymentRequestId AND Status='ACTIVE';
 SELECT @txUsed=ISNULL(SUM(AllocatedAmount),0) FROM dbo.PaymentAllocations WITH(UPDLOCK,HOLDLOCK) WHERE PaymentTransactionId=@PaymentTransactionId AND Status='ACTIVE';
 SET @amt=COALESCE(@AllocatedAmount,CASE WHEN @tx-@txUsed<@due-@reqPaid THEN @tx-@txUsed ELSE @due-@reqPaid END);
 IF @amt<=0 OR @amt>@tx-@txUsed OR @amt>@due-@reqPaid THROW 51413,'Allocation exceeds remaining transaction or receivable amount.',1;
 INSERT dbo.PaymentAllocations(Id,PartnerId,PaymentRequestId,PaymentTransactionId,BankTransactionId,AllocatedAmount,Status,AllocatedAt)
 VALUES(NEWID(),@PartnerId,@PaymentRequestId,@PaymentTransactionId,NULL,@amt,'ACTIVE',SYSUTCDATETIME());
 SET @reqPaid=@reqPaid+@amt;
 UPDATE dbo.PaymentRequests SET AmountPaid=@reqPaid,Status=CASE WHEN @reqPaid=0 THEN 'PENDING' WHEN @reqPaid<@due THEN 'PARTIALLY_PAID' ELSE 'PAID' END,UpdatedAt=SYSUTCDATETIME() WHERE Id=@PaymentRequestId;
 COMMIT;
END
GO
IF OBJECT_ID(N'dbo.sp_ReversePaymentAllocationV2',N'P') IS NULL EXEC('CREATE PROCEDURE dbo.sp_ReversePaymentAllocationV2 AS BEGIN SET NOCOUNT ON; END');
GO
ALTER PROCEDURE dbo.sp_ReversePaymentAllocationV2 @PartnerId uniqueidentifier,@AllocationId uniqueidentifier,@Reason nvarchar(500)
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON; BEGIN TRAN;
 DECLARE @r uniqueidentifier,@due decimal(18,2),@paid decimal(18,2);
 SELECT @r=PaymentRequestId FROM dbo.PaymentAllocations WITH(UPDLOCK,HOLDLOCK) WHERE Id=@AllocationId AND PartnerId=@PartnerId AND Status='ACTIVE';
 IF @r IS NULL THROW 51414,'Active allocation not found.',1;
 UPDATE dbo.PaymentAllocations SET Status='REVERSED',ReversedAt=SYSUTCDATETIME(),ReversalReason=@Reason WHERE Id=@AllocationId;
 SELECT @due=AmountDue FROM dbo.PaymentRequests WITH(UPDLOCK,HOLDLOCK) WHERE Id=@r AND PartnerId=@PartnerId;
 SELECT @paid=ISNULL(SUM(AllocatedAmount),0) FROM dbo.PaymentAllocations WHERE PaymentRequestId=@r AND Status='ACTIVE';
 UPDATE dbo.PaymentRequests SET AmountPaid=@paid,Status=CASE WHEN @paid=0 THEN 'PENDING' WHEN @paid<@due THEN 'PARTIALLY_PAID' ELSE 'PAID' END,UpdatedAt=SYSUTCDATETIME() WHERE Id=@r;
 COMMIT;
END
GO
PRINT '041-generic-payment-cutover.sql: OK';
GO