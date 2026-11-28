-- CUSTOMER-PORTAL-FOUNDATION - Saved e-invoice templates per provider (operator 2026-11-28: "tạo ra một danh sách mẫu hóa đơn ...
-- chọn mặc định mẫu hóa đơn nào thì mẫu hóa đơn đó sẽ được dùng; mẫu chạy theo nhà cung cấp").
-- Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261128T043400Z-BA4498.
--   dbo.CustomerInvoiceTemplates: the templates (mẫu số + ký hiệu) a company has used, per provider and environment
--   (LIVE / DEMO). The default stays dbo.CustomerInvoiceSettings.InvoicePattern/InvoiceSeries.
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, after 069.
-- Adds one table; no existing row is changed. Idempotent.
-- Verify: database/scripts/127-verify-customer-invoice-templates.sql. Rollback: database/scripts/128-rollback-customer-invoice-templates.sql.
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
IF OBJECT_ID(N'dbo.CustomerInvoiceTemplates', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustomerInvoiceTemplates (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_CustomerInvoiceTemplates PRIMARY KEY CONSTRAINT DF_CIT_Id DEFAULT NEWID(),
        CustomerId uniqueidentifier NOT NULL CONSTRAINT FK_CIT_Customer REFERENCES dbo.Customers(Id),
        Provider varchar(20) NOT NULL,
        Environment varchar(10) NOT NULL CONSTRAINT DF_CIT_Environment DEFAULT 'LIVE',
        InvoicePattern nvarchar(50) NOT NULL,
        InvoiceSeries nvarchar(20) NOT NULL,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_CIT_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        CONSTRAINT CK_CIT_Environment CHECK (Environment IN ('LIVE','DEMO'))
    );
    CREATE UNIQUE INDEX UX_CIT_Template ON dbo.CustomerInvoiceTemplates(CustomerId, Provider, Environment, InvoicePattern, InvoiceSeries);
END
COMMIT;
PRINT N'070-customer-invoice-templates.sql: OK';
