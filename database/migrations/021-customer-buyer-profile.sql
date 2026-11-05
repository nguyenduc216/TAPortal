-- CUSTOMER-PORTAL-FOUNDATION — flexible buyer profile (operator request 2026-11-06). MIGRATION_NOT_EXECUTED.
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, after 020.
-- dbo.CustomerBuyers (a company's own customers in the Customer Portal) gets:
--   BuyerType          INDIVIDUAL (khách lẻ) / HOUSEHOLD (hộ kinh doanh) / BUSINESS (doanh nghiệp) / ORGANIZATION (tổ chức khác);
--                      NULL on existing rows = derived by the app (tax code present ⇒ BUSINESS, else INDIVIDUAL), so no
--                      existing row is updated here.
--   BuyerGroup         free text group (nhà hàng, khách sạn, bán lẻ ...).
--   IdentityNumber     CCCD/CMND/passport of an individual buyer (e-invoice buyer identification when there is no tax code).
--   Zalo               Zalo phone or name.
--   BankName, BankAccountNumber, BankAccountName   the buyer's bank account (refunds, reconciliation).
-- Adds nullable columns only. Idempotent. Verify: database/scripts/031-verify-customer-buyer-profile.sql.
-- Rollback: database/scripts/032-rollback-customer-buyer-profile.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
-- Equivalent required positive guard: DB_NAME() = 'TAPortalSys'. No USE statement.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.CustomerBuyers', N'U') IS NULL
 THROW 51001, 'Precondition failed: dbo.CustomerBuyers missing (run 012 first)', 1;

IF COL_LENGTH(N'dbo.CustomerBuyers', N'BuyerType') IS NULL
BEGIN
    ALTER TABLE dbo.CustomerBuyers ADD
        BuyerType varchar(20) NULL CONSTRAINT CK_CB_BuyerType CHECK (BuyerType IN ('INDIVIDUAL','HOUSEHOLD','BUSINESS','ORGANIZATION')),
        BuyerGroup nvarchar(100) NULL,
        IdentityNumber varchar(20) NULL,
        Zalo nvarchar(100) NULL,
        BankName nvarchar(100) NULL,
        BankAccountNumber varchar(30) NULL,
        BankAccountName nvarchar(150) NULL;
    PRINT N'dbo.CustomerBuyers: 7 buyer profile columns added';
END ELSE PRINT N'dbo.CustomerBuyers: buyer profile columns exist';

COMMIT;
PRINT N'021-customer-buyer-profile.sql: OK';
