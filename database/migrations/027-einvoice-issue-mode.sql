-- CUSTOMER-PORTAL-FOUNDATION — e-invoice issue mode and HSM confirmation (operator request 2026-11-07). MIGRATION_NOT_EXECUTED.
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, after 026.
-- MISA signs invoices issued through the API only with its HSM signature. The company picks how paid orders are invoiced:
--   dbo.CustomerInvoiceSettings  IssueMode       varchar(10)      NULL  SIGNED (HSM bought) | DRAFT (drafts, signed by hand) | NONE
--                                HsmConfirmedAt  datetime2(0)     NULL  when the company confirmed it bought the HSM (UTC)
--                                HsmConfirmedBy  uniqueidentifier NULL  who confirmed it
-- Existing rows keep NULL: the applications read NULL as SIGNED when AutoIssueEnabled = 1, otherwise NONE (no row is changed).
-- Nullable columns and a CHECK only. Idempotent.
-- Verify: database/scripts/043-verify-einvoice-issue-mode.sql. Rollback: database/scripts/044-rollback-einvoice-issue-mode.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
-- Equivalent required positive guard: DB_NAME() = 'TAPortalSys'. No USE statement.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.CustomerInvoiceSettings', N'U') IS NULL
 THROW 51001, 'Precondition failed: dbo.CustomerInvoiceSettings missing (run 022 first)', 1;
IF COL_LENGTH(N'dbo.CustomerInvoiceSettings', N'IssueMode') IS NULL
    ALTER TABLE dbo.CustomerInvoiceSettings ADD IssueMode varchar(10) NULL;
IF COL_LENGTH(N'dbo.CustomerInvoiceSettings', N'HsmConfirmedAt') IS NULL
    ALTER TABLE dbo.CustomerInvoiceSettings ADD HsmConfirmedAt datetime2(0) NULL;
IF COL_LENGTH(N'dbo.CustomerInvoiceSettings', N'HsmConfirmedBy') IS NULL
    ALTER TABLE dbo.CustomerInvoiceSettings ADD HsmConfirmedBy uniqueidentifier NULL;
IF OBJECT_ID(N'dbo.CK_CIS_IssueMode', N'C') IS NULL
    EXEC sys.sp_executesql N'ALTER TABLE dbo.CustomerInvoiceSettings WITH CHECK ADD CONSTRAINT CK_CIS_IssueMode CHECK (IssueMode IS NULL OR IssueMode IN (''SIGNED'',''DRAFT'',''NONE''))';
COMMIT;
PRINT N'027-einvoice-issue-mode.sql: OK';
