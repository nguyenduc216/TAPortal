-- CUSTOMER-PORTAL-FOUNDATION — ROLLBACK of migrations/027-einvoice-issue-mode.sql.
-- Drops CK_CIS_IssueMode, IssueMode, HsmConfirmedAt and HsmConfirmedBy. Refuses once a company has chosen a mode or
-- confirmed its HSM purchase (that record must not be lost): decide with the operator first.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @used int = 0;
IF COL_LENGTH(N'dbo.CustomerInvoiceSettings', N'IssueMode') IS NOT NULL AND COL_LENGTH(N'dbo.CustomerInvoiceSettings', N'HsmConfirmedAt') IS NOT NULL
    EXEC sys.sp_executesql N'SELECT @n = COUNT(*) FROM dbo.CustomerInvoiceSettings WHERE IssueMode IS NOT NULL OR HsmConfirmedAt IS NOT NULL', N'@n int OUTPUT', @n = @used OUTPUT;
IF @used > 0
    THROW 51002, 'Rollback refused: companies have chosen an issue mode or confirmed an HSM purchase', 1;
IF OBJECT_ID(N'dbo.CK_CIS_IssueMode', N'C') IS NOT NULL ALTER TABLE dbo.CustomerInvoiceSettings DROP CONSTRAINT CK_CIS_IssueMode;
IF COL_LENGTH(N'dbo.CustomerInvoiceSettings', N'IssueMode') IS NOT NULL ALTER TABLE dbo.CustomerInvoiceSettings DROP COLUMN IssueMode;
IF COL_LENGTH(N'dbo.CustomerInvoiceSettings', N'HsmConfirmedAt') IS NOT NULL ALTER TABLE dbo.CustomerInvoiceSettings DROP COLUMN HsmConfirmedAt;
IF COL_LENGTH(N'dbo.CustomerInvoiceSettings', N'HsmConfirmedBy') IS NOT NULL ALTER TABLE dbo.CustomerInvoiceSettings DROP COLUMN HsmConfirmedBy;
COMMIT;
PRINT N'044-rollback-einvoice-issue-mode.sql: OK';
