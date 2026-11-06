-- CUSTOMER-PORTAL-FOUNDATION — ROLLBACK of migrations/023-einvoice-template-id.sql.
-- InvoicePattern varchar(50) -> varchar(20). Refuses when a saved template id is longer than 20 characters (it would be cut):
-- export it and decide with the operator first.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH(N'dbo.CustomerInvoiceSettings', N'InvoicePattern') > 20
BEGIN
    DECLARE @long int = 0;
    EXEC sys.sp_executesql N'SELECT @n = COUNT(*) FROM dbo.CustomerInvoiceSettings WHERE LEN(InvoicePattern) > 20', N'@n int OUTPUT', @n = @long OUTPUT;
    IF @long > 0
        THROW 51002, 'Rollback refused: saved template ids are longer than 20 characters; export them first', 1;
    ALTER TABLE dbo.CustomerInvoiceSettings ALTER COLUMN InvoicePattern varchar(20) NULL;
END
COMMIT;
PRINT N'036-rollback-einvoice-template-id.sql: OK';
