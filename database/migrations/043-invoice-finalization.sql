/* V1 RC3 043 - invoice snapshot freeze and exactly-once finalization contract. */
USE [TAPortal];
GO
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO
IF COL_LENGTH('dbo.InvoiceRequests','SnapshotFrozenAt') IS NULL ALTER TABLE dbo.InvoiceRequests ADD SnapshotFrozenAt datetime2(3) NULL;
IF COL_LENGTH('dbo.InvoiceRequests','SnapshotVersion') IS NULL ALTER TABLE dbo.InvoiceRequests ADD SnapshotVersion int NOT NULL CONSTRAINT DF_IR_SnapshotVersion DEFAULT 1;
GO
IF OBJECT_ID(N'dbo.tr_InvoiceRequest_FrozenSnapshot',N'TR') IS NULL EXEC('CREATE TRIGGER dbo.tr_InvoiceRequest_FrozenSnapshot ON dbo.InvoiceRequests AFTER UPDATE AS BEGIN SET NOCOUNT ON; IF UPDATE(SellerLegalName) OR UPDATE(SellerTaxCode) OR UPDATE(SellerAddress) OR UPDATE(BuyerNameSnapshot) OR UPDATE(BuyerTaxCodeSnapshot) OR UPDATE(BuyerAddressSnapshot) OR UPDATE(SubtotalAmount) OR UPDATE(DiscountAmount) OR UPDATE(VatAmount) OR UPDATE(OtherChargesAmount) OR UPDATE(GrandTotalAmount) BEGIN IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id WHERE d.SnapshotFrozenAt IS NOT NULL) THROW 51430,''Frozen invoice snapshot cannot be modified.'',1; END END');
GO
IF OBJECT_ID(N'dbo.sp_FreezeInvoiceRequest',N'P') IS NULL EXEC('CREATE PROCEDURE dbo.sp_FreezeInvoiceRequest AS BEGIN SET NOCOUNT ON; END');
GO
ALTER PROCEDURE dbo.sp_FreezeInvoiceRequest @PartnerId uniqueidentifier,@InvoiceRequestId uniqueidentifier
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON; BEGIN TRAN;
 DECLARE @sub decimal(18,2),@disc decimal(18,2),@vat decimal(18,2),@other decimal(18,2),@grand decimal(18,2);
 SELECT @sub=SubtotalAmount,@disc=DiscountAmount,@vat=VatAmount,@other=OtherChargesAmount,@grand=GrandTotalAmount FROM dbo.InvoiceRequests WITH(UPDLOCK,HOLDLOCK) WHERE Id=@InvoiceRequestId AND PartnerId=@PartnerId AND SnapshotFrozenAt IS NULL;
 IF @sub IS NULL OR @grand IS NULL THROW 51431,'Invoice totals are incomplete.',1;
 IF ABS((@sub-ISNULL(@disc,0)+ISNULL(@vat,0)+ISNULL(@other,0))-@grand)>0.01 THROW 51432,'Invoice totals do not reconcile.',1;
 IF EXISTS(SELECT 1 FROM dbo.InvoiceItems WHERE InvoiceRequestId=@InvoiceRequestId AND (LineTotal IS NULL OR VatAmount IS NULL OR TaxableAmount IS NULL)) THROW 51433,'Invoice item legal totals are incomplete.',1;
 UPDATE dbo.InvoiceRequests SET SnapshotFrozenAt=SYSUTCDATETIME(),Status=CASE WHEN Status='PENDING_REVIEW' THEN 'READY' ELSE Status END WHERE Id=@InvoiceRequestId AND PartnerId=@PartnerId;
 COMMIT;
END
GO
IF OBJECT_ID(N'dbo.sp_FinalizeIssuedInvoiceCredit',N'P') IS NULL EXEC('CREATE PROCEDURE dbo.sp_FinalizeIssuedInvoiceCredit AS BEGIN SET NOCOUNT ON; END');
GO
ALTER PROCEDURE dbo.sp_FinalizeIssuedInvoiceCredit @PartnerId uniqueidentifier,@InvoiceRequestId uniqueidentifier,@InvoiceId uniqueidentifier
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON; BEGIN TRAN;
 DECLARE @c uniqueidentifier,@status varchar(20);
 SELECT @c=Id,@status=Status FROM dbo.InvoiceCreditConsumptions WITH(UPDLOCK,HOLDLOCK) WHERE PartnerId=@PartnerId AND InvoiceRequestId=@InvoiceRequestId;
 IF @c IS NULL THROW 51434,'Invoice credit reservation binding not found.',1;
 IF @status='CONSUMED' BEGIN COMMIT; RETURN; END;
 IF @status<>'RESERVED' THROW 51435,'Invoice credit is not reservable for consumption.',1;
 UPDATE dbo.InvoiceCreditConsumptions SET InvoiceId=@InvoiceId,Status='CONSUMED',ConsumedAt=SYSUTCDATETIME() WHERE Id=@c;
 UPDATE dbo.InvoiceRequests SET Status='ISSUED' WHERE Id=@InvoiceRequestId AND PartnerId=@PartnerId;
 COMMIT;
END
GO
PRINT '043-invoice-finalization.sql: OK';
GO