-- CUSTOMER-PROFILE — Admin audit columns on the legacy dbo.AuditLogs table (operator report 2026-11-06). MIGRATION_NOT_EXECUTED.
-- Change: CUSTOMER-PROFILE-CHG-20261106T145317Z-17A9E4.
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, after 024.
-- Production dbo.AuditLogs predates migrations 006/010 (legacy columns ModuleCode, FunctionCode, ActionCode, EntityType).
-- TAPortal.Shared.AuditSql writes Action and EntityName, so every Admin audit row was refused (schema mismatch) and
-- "Hiện"/"Sao chép" on a customer password, which requires a durable audit row, always failed.
--   dbo.AuditLogs  Action      nvarchar(100) NULL   action written by the applications (legacy rows keep ActionCode)
--                  EntityName  nvarchar(200) NULL   entity written by the applications (legacy rows keep EntityType)
-- Nullable columns only; no existing row is changed. Idempotent.
-- Verify: database/scripts/039-verify-audit-logs-admin-columns.sql. Rollback: database/scripts/040-rollback-audit-logs-admin-columns.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
-- Equivalent required positive guard: DB_NAME() = 'TAPortalSys'. No USE statement.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.AuditLogs', N'U') IS NULL
 THROW 51001, 'Precondition failed: dbo.AuditLogs missing (see migration 010)', 1;

IF COL_LENGTH(N'dbo.AuditLogs', N'Action') IS NULL
    ALTER TABLE dbo.AuditLogs ADD Action nvarchar(100) NULL;
IF COL_LENGTH(N'dbo.AuditLogs', N'EntityName') IS NULL
    ALTER TABLE dbo.AuditLogs ADD EntityName nvarchar(200) NULL;
COMMIT;
PRINT N'025-audit-logs-admin-columns.sql: OK';
