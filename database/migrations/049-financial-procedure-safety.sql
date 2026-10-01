/* V1 RC5 049 - mandatory financial procedure rollback safety. SQL Server 2014 compatible. */
USE [TAPortal];
GO
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO
ALTER PROCEDURE dbo.sp_FreezeInvoiceRequest @PartnerId uniqueidentifier,@InvoiceRequestId uniqueidentifier
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRAN;
  DECLARE @sub decimal(18,2),@disc decimal(18,2),@vat decimal(18,2),@other decimal(18,2),@grand decimal(18,2);
  SELECT @sub=SubtotalAmount,@disc=DiscountAmount,@vat=VatAmount,@other=OtherChargesAmount,@grand=GrandTotalAmount
  FROM dbo.InvoiceRequests WITH(UPDLOCK,HOLDLOCK)
  WHERE Id=@InvoiceRequestId AND PartnerId=@PartnerId AND SnapshotFrozenAt IS NULL;
  IF @sub IS NULL OR @grand IS NULL THROW 51801,'Invoice totals are incomplete or request is unavailable/frozen.',1;
  IF ABS((@sub-ISNULL(@disc,0)+ISNULL(@vat,0)+ISNULL(@other,0))-@grand)>0.01 THROW 51802,'Invoice totals do not reconcile.',1;
  IF EXISTS(SELECT 1 FROM dbo.InvoiceItems WHERE InvoiceRequestId=@InvoiceRequestId AND (LineTotal IS NULL OR VatAmount IS NULL OR TaxableAmount IS NULL)) THROW 51803,'Invoice item legal totals are incomplete.',1;
  UPDATE dbo.InvoiceRequests SET SnapshotFrozenAt=SYSUTCDATETIME(),Status=CASE WHEN Status='PENDING_REVIEW' THEN 'READY' ELSE Status END WHERE Id=@InvoiceRequestId AND PartnerId=@PartnerId;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END
GO
PRINT '049-financial-procedure-safety.sql: OK';
GO