-- CUSTOMER-PORTAL-FOUNDATION - E-invoice demo and production side by side (operator 2026-11-28: "có môi trường demo và môi
-- trường production ... có thể chuyển qua lại cho hai môi trường, cần tách biệt phần này").
-- Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261128T050250Z-AC5601.
--   dbo.CustomerInvoiceConnections: one login per company, provider and environment (LIVE / DEMO) for providers called
--   directly (SoftDreams EasyInvoice); ProviderSecret is the password encrypted by the application, as in CustomerInvoiceSettings.
--   The connection in use stays in dbo.CustomerInvoiceSettings; switching copies the other environment's login there.
--   dbo.CustomerInvoiceTemplates.IsDefault: the default template of each environment, restored when switching back.
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, after 070.
-- Adds one table and one column with a default; no existing row is changed. Idempotent.
-- Verify: database/scripts/129-verify-einvoice-environments.sql. Rollback: database/scripts/130-rollback-einvoice-environments.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
-- Equivalent required positive guard: DB_NAME() = 'TAPortalSys'. No USE statement.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.CustomerInvoiceTemplates', N'U') IS NULL
 THROW 51001, 'Precondition failed: dbo.CustomerInvoiceTemplates missing (run 070 first)', 1;
IF COL_LENGTH(N'dbo.CustomerInvoiceTemplates', N'IsDefault') IS NULL
    ALTER TABLE dbo.CustomerInvoiceTemplates ADD IsDefault bit NOT NULL CONSTRAINT DF_CIT_IsDefault DEFAULT 0;
IF OBJECT_ID(N'dbo.CustomerInvoiceConnections', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustomerInvoiceConnections (
        CustomerId uniqueidentifier NOT NULL CONSTRAINT FK_CIC_Customer REFERENCES dbo.Customers(Id),
        Provider varchar(20) NOT NULL,
        Environment varchar(10) NOT NULL,
        ProviderTaxCode nvarchar(20) NULL,
        ProviderUsername nvarchar(150) NULL,
        ProviderSecret varbinary(max) NULL,
        ConnectedAt datetime2(3) NULL,
        UpdatedAt datetime2(3) NOT NULL CONSTRAINT DF_CIC_UpdatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedBy uniqueidentifier NULL,
        CONSTRAINT PK_CustomerInvoiceConnections PRIMARY KEY (CustomerId, Provider, Environment),
        CONSTRAINT CK_CIC_Environment CHECK (Environment IN ('LIVE','DEMO'))
    );
END
COMMIT;
PRINT N'071-einvoice-environments.sql: OK';
