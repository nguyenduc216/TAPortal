-- CONTRACTS-AND-TASKS - staff assigned to a customer (operator decisions 2026-11-13: each customer has at most one
-- managing employee and any number of executing employees; the task list filters customers by the chosen employees).
-- MIGRATION_NOT_EXECUTED.
-- Change: CONTRACTS-AND-TASKS-CHG-20261113T114748Z-364101.
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, after 038
-- (039 and 040 were one-off data migrations, not kept in Git; their numbers stay reserved).
--   dbo.CustomerStaff   one row per (customer, employee, role); Role MANAGER (at most one per customer, filtered unique
--                       index) or EXECUTOR. Edited on Khách hàng > Tổng quan > "Nhân sự phụ trách" (CRM.CUSTOMERS.UPDATE).
-- New table only; no existing row or column is changed. Idempotent.
-- Verify: database/scripts/071-verify-customer-staff.sql. Rollback: database/scripts/072-rollback-customer-staff.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.Customers', N'U') IS NULL OR OBJECT_ID(N'dbo.Employees', N'U') IS NULL
 THROW 51001, 'Precondition failed: dbo.Customers or dbo.Employees missing', 1;

IF OBJECT_ID(N'dbo.CustomerStaff', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustomerStaff (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_CustomerStaff PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        CustomerId uniqueidentifier NOT NULL,
        EmployeeId uniqueidentifier NOT NULL,
        Role varchar(10) NOT NULL,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_CustomerStaff_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        CONSTRAINT FK_CustomerStaff_Customer FOREIGN KEY (CustomerId) REFERENCES dbo.Customers(Id),
        CONSTRAINT FK_CustomerStaff_Employee FOREIGN KEY (EmployeeId) REFERENCES dbo.Employees(Id),
        CONSTRAINT CK_CustomerStaff_Role CHECK (Role IN ('MANAGER','EXECUTOR')),
        CONSTRAINT UQ_CustomerStaff UNIQUE (CustomerId, EmployeeId, Role)
    );
    CREATE UNIQUE INDEX UX_CustomerStaff_Manager ON dbo.CustomerStaff(CustomerId) WHERE Role = 'MANAGER';
    CREATE INDEX IX_CustomerStaff_Employee ON dbo.CustomerStaff(EmployeeId) INCLUDE (CustomerId, Role);
    PRINT N'dbo.CustomerStaff: created';
END
COMMIT TRANSACTION;
PRINT N'041-customer-staff.sql: OK';
