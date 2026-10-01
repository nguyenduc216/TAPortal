/* TAPortal 017 - Atomic payment allocation and reconciliation procedures. */
USE [TAPortal];
GO
IF OBJECT_ID(N'dbo.sp_AllocateBankTransaction',N'P') IS NULL EXEC('CREATE PROCEDURE dbo.sp_AllocateBankTransaction AS BEGIN SET NOCOUNT ON; END');
GO
ALTER PROCEDURE dbo.sp_AllocateBankTransaction
 @PaymentRequestId uniqueidentifier,
 @BankTransactionId uniqueidentifier,
 @AllocatedAmount decimal(18,2)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON; SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
 BEGIN TRAN;
 DECLARE @due decimal(18,2),@txAmount decimal(18,2),@direction varchar(10),@partnerP uniqueidentifier,@partnerT uniqueidentifier,@alreadyTx decimal(18,2),@amount decimal(18,2);
 SELECT @due=AmountDue,@partnerP=PartnerId FROM dbo.PaymentRequests WITH(UPDLOCK,HOLDLOCK) WHERE Id=@PaymentRequestId AND Status NOT IN('CANCELLED','EXPIRED');
 IF @due IS NULL THROW 51001,'Payment request not found or not allocatable.',1;
 SELECT @txAmount=Amount,@direction=Direction,@partnerT=PartnerId FROM dbo.BankTransactions WITH(UPDLOCK,HOLDLOCK) WHERE Id=@BankTransactionId;
 IF @txAmount IS NULL THROW 51002,'Bank transaction not found.',1;
 IF @direction<>'CREDIT' THROW 51003,'Only CREDIT transactions can be allocated to receivables.',1;
 IF @partnerP<>@partnerT THROW 51004,'Cross-partner allocation is forbidden.',1;
 SELECT @alreadyTx=ISNULL(SUM(AllocatedAmount),0) FROM dbo.PaymentAllocations WITH(UPDLOCK,HOLDLOCK) WHERE BankTransactionId=@BankTransactionId AND Status='ACTIVE';
 SET @amount=COALESCE(@AllocatedAmount,@txAmount-@alreadyTx);
 IF @amount<=0 THROW 51005,'Allocation amount must be positive.',1;
 IF @alreadyTx+@amount>@txAmount THROW 51006,'Allocations exceed bank transaction amount.',1;
 IF EXISTS(SELECT 1 FROM dbo.PaymentAllocations WHERE PaymentRequestId=@PaymentRequestId AND BankTransactionId=@BankTransactionId AND Status='ACTIVE') THROW 51007,'This transaction is already actively allocated to this payment request.',1;
 INSERT dbo.PaymentAllocations(PaymentRequestId,BankTransactionId,AllocatedAmount) VALUES(@PaymentRequestId,@BankTransactionId,@amount);
 DECLARE @paid decimal(18,2); SELECT @paid=ISNULL(SUM(AllocatedAmount),0) FROM dbo.PaymentAllocations WHERE PaymentRequestId=@PaymentRequestId AND Status='ACTIVE';
 UPDATE dbo.PaymentRequests SET AmountPaid=@paid,Status=CASE WHEN @paid=0 THEN 'PENDING' WHEN @paid<AmountDue THEN 'PARTIALLY_PAID' WHEN @paid=AmountDue THEN 'PAID' ELSE 'OVERPAID' END,ClosedAt=CASE WHEN @paid>=AmountDue THEN COALESCE(ClosedAt,SYSUTCDATETIME()) ELSE NULL END,UpdatedAt=SYSUTCDATETIME() WHERE Id=@PaymentRequestId;
 COMMIT;
 SELECT * FROM dbo.vw_PaymentRequestBalances WHERE Id=@PaymentRequestId;
END
GO
IF OBJECT_ID(N'dbo.sp_ReversePaymentAllocation',N'P') IS NULL EXEC('CREATE PROCEDURE dbo.sp_ReversePaymentAllocation AS BEGIN SET NOCOUNT ON; END');
GO
ALTER PROCEDURE dbo.sp_ReversePaymentAllocation
 @PaymentAllocationId uniqueidentifier,@Reason nvarchar(500)
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRAN;
 DECLARE @request uniqueidentifier;
 SELECT @request=PaymentRequestId FROM dbo.PaymentAllocations WITH(UPDLOCK) WHERE Id=@PaymentAllocationId AND Status='ACTIVE';
 IF @request IS NULL THROW 51011,'Active allocation not found.',1;
 UPDATE dbo.PaymentAllocations SET Status='REVERSED',ReversedAt=SYSUTCDATETIME(),ReversalReason=@Reason WHERE Id=@PaymentAllocationId;
 DECLARE @paid decimal(18,2); SELECT @paid=ISNULL(SUM(AllocatedAmount),0) FROM dbo.PaymentAllocations WHERE PaymentRequestId=@request AND Status='ACTIVE';
 UPDATE dbo.PaymentRequests SET AmountPaid=@paid,Status=CASE WHEN @paid=0 THEN 'PENDING' WHEN @paid<AmountDue THEN 'PARTIALLY_PAID' WHEN @paid=AmountDue THEN 'PAID' ELSE 'OVERPAID' END,ClosedAt=CASE WHEN @paid>=AmountDue THEN COALESCE(ClosedAt,SYSUTCDATETIME()) ELSE NULL END,UpdatedAt=SYSUTCDATETIME() WHERE Id=@request AND Status NOT IN('CANCELLED','EXPIRED','NEEDS_REVIEW');
 COMMIT;
 SELECT * FROM dbo.vw_PaymentRequestBalances WHERE Id=@request;
END
GO
PRINT '017-payment-allocation-procedures.sql: OK';
GO