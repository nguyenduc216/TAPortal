/* RC2 036 - Immutable legal invoice snapshot and totals. */
USE [TAPortal]; GO
IF COL_LENGTH('dbo.InvoiceRequests','SellerLegalName') IS NULL ALTER TABLE dbo.InvoiceRequests ADD SellerLegalName nvarchar(300) NULL;
IF COL_LENGTH('dbo.InvoiceRequests','SellerTaxCode') IS NULL ALTER TABLE dbo.InvoiceRequests ADD SellerTaxCode varchar(50) NULL;
IF COL_LENGTH('dbo.InvoiceRequests','SellerAddress') IS NULL ALTER TABLE dbo.InvoiceRequests ADD SellerAddress nvarchar(1000) NULL;
IF COL_LENGTH('dbo.InvoiceRequests','BuyerNameSnapshot') IS NULL ALTER TABLE dbo.InvoiceRequests ADD BuyerNameSnapshot nvarchar(300) NULL;
IF COL_LENGTH('dbo.InvoiceRequests','BuyerTaxCodeSnapshot') IS NULL ALTER TABLE dbo.InvoiceRequests ADD BuyerTaxCodeSnapshot varchar(50) NULL;
IF COL_LENGTH('dbo.InvoiceRequests','BuyerAddressSnapshot') IS NULL ALTER TABLE dbo.InvoiceRequests ADD BuyerAddressSnapshot nvarchar(1000) NULL;
IF COL_LENGTH('dbo.InvoiceRequests','SubtotalAmount') IS NULL ALTER TABLE dbo.InvoiceRequests ADD SubtotalAmount decimal(18,2) NULL;
IF COL_LENGTH('dbo.InvoiceRequests','DiscountAmount') IS NULL ALTER TABLE dbo.InvoiceRequests ADD DiscountAmount decimal(18,2) NOT NULL CONSTRAINT DF_IR_Discount DEFAULT 0;
IF COL_LENGTH('dbo.InvoiceRequests','VatAmount') IS NULL ALTER TABLE dbo.InvoiceRequests ADD VatAmount decimal(18,2) NOT NULL CONSTRAINT DF_IR_Vat DEFAULT 0;
IF COL_LENGTH('dbo.InvoiceRequests','OtherChargesAmount') IS NULL ALTER TABLE dbo.InvoiceRequests ADD OtherChargesAmount decimal(18,2) NOT NULL CONSTRAINT DF_IR_Other DEFAULT 0;
IF COL_LENGTH('dbo.InvoiceRequests','GrandTotalAmount') IS NULL ALTER TABLE dbo.InvoiceRequests ADD GrandTotalAmount decimal(18,2) NULL;
IF COL_LENGTH('dbo.InvoiceRequests','RequestedIssueDate') IS NULL ALTER TABLE dbo.InvoiceRequests ADD RequestedIssueDate date NULL;
GO
IF COL_LENGTH('dbo.InvoiceItems','DiscountAmount') IS NULL ALTER TABLE dbo.InvoiceItems ADD DiscountAmount decimal(18,2) NOT NULL CONSTRAINT DF_II_Discount DEFAULT 0;
IF COL_LENGTH('dbo.InvoiceItems','TaxableAmount') IS NULL ALTER TABLE dbo.InvoiceItems ADD TaxableAmount decimal(18,2) NULL;
IF COL_LENGTH('dbo.InvoiceItems','VatAmount') IS NULL ALTER TABLE dbo.InvoiceItems ADD VatAmount decimal(18,2) NULL;
IF COL_LENGTH('dbo.InvoiceItems','LineTotal') IS NULL ALTER TABLE dbo.InvoiceItems ADD LineTotal decimal(18,2) NULL;
GO
IF OBJECT_ID(N'dbo.InvoiceRequestReceivables',N'U') IS NULL CREATE TABLE dbo.InvoiceRequestReceivables(InvoiceRequestId uniqueidentifier NOT NULL,PaymentRequestId uniqueidentifier NOT NULL,LinkedAmount decimal(18,2) NULL,CreatedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),CONSTRAINT PK_IRR PRIMARY KEY(InvoiceRequestId,PaymentRequestId),CONSTRAINT FK_IRR_InvoiceRequest FOREIGN KEY(InvoiceRequestId) REFERENCES dbo.InvoiceRequests(Id),CONSTRAINT FK_IRR_PaymentRequest FOREIGN KEY(PaymentRequestId) REFERENCES dbo.PaymentRequests(Id),CONSTRAINT CK_IRR_Amount CHECK(LinkedAmount IS NULL OR LinkedAmount>0));
GO
IF OBJECT_ID(N'dbo.InvoiceCreditConsumptions',N'U') IS NULL BEGIN
 CREATE TABLE dbo.InvoiceCreditConsumptions(Id uniqueidentifier NOT NULL DEFAULT NEWSEQUENTIALID() CONSTRAINT PK_InvoiceCreditConsumptions PRIMARY KEY,PartnerId uniqueidentifier NOT NULL,InvoiceRequestId uniqueidentifier NOT NULL,CreditReservationId uniqueidentifier NOT NULL,InvoiceId uniqueidentifier NULL,Status varchar(20) NOT NULL DEFAULT 'RESERVED',IdempotencyKey nvarchar(200) NOT NULL,CreatedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),ConsumedAt datetime2(3) NULL,ReleasedAt datetime2(3) NULL,CONSTRAINT FK_ICC_Partner FOREIGN KEY(PartnerId) REFERENCES dbo.Partners(Id),CONSTRAINT FK_ICC_Request FOREIGN KEY(InvoiceRequestId) REFERENCES dbo.InvoiceRequests(Id),CONSTRAINT FK_ICC_Reservation FOREIGN KEY(CreditReservationId) REFERENCES dbo.CreditReservations(Id),CONSTRAINT FK_ICC_Invoice FOREIGN KEY(InvoiceId) REFERENCES dbo.Invoices(Id),CONSTRAINT CK_ICC_Status CHECK(Status IN('RESERVED','CONSUMED','RELEASED','FAILED')));
 CREATE UNIQUE INDEX UX_ICC_Request ON dbo.InvoiceCreditConsumptions(InvoiceRequestId);
 CREATE UNIQUE INDEX UX_ICC_Idem ON dbo.InvoiceCreditConsumptions(PartnerId,IdempotencyKey);
END
GO
PRINT '036-invoice-legal-snapshot.sql: OK'; GO