-- CUSTOMER-PORTAL-FOUNDATION — ROLLBACK of migrations/021-customer-buyer-profile.sql.
-- Drops the 7 buyer profile columns of dbo.CustomerBuyers. Refuses when any of them holds data (entered by customers):
-- export and decide with the operator first.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH(N'dbo.CustomerBuyers', N'BuyerType') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.CustomerBuyers') AND name = N'BankAccountName')
    BEGIN
        DECLARE @used int;
        EXEC sys.sp_executesql N'SELECT @n = COUNT(*) FROM dbo.CustomerBuyers WHERE BuyerType IS NOT NULL OR BuyerGroup IS NOT NULL OR IdentityNumber IS NOT NULL
            OR Zalo IS NOT NULL OR BankName IS NOT NULL OR BankAccountNumber IS NOT NULL OR BankAccountName IS NOT NULL', N'@n int OUTPUT', @n = @used OUTPUT;
        IF @used > 0
            THROW 51002, 'Rollback refused: buyer profile columns hold customer data; export them first', 1;
    END
    IF OBJECT_ID(N'dbo.CK_CB_BuyerType', N'C') IS NOT NULL ALTER TABLE dbo.CustomerBuyers DROP CONSTRAINT CK_CB_BuyerType;
    ALTER TABLE dbo.CustomerBuyers DROP COLUMN BuyerType, BuyerGroup, IdentityNumber, Zalo, BankName, BankAccountNumber, BankAccountName;
END
COMMIT;
PRINT N'032-rollback-customer-buyer-profile.sql: OK';
