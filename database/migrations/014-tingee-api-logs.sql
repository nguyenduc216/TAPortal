-- TINGEE-INTEGRATION-CHG-20261104T040031Z-0CF9F3 — Log of every outgoing Tingee Open API call (dbo.TingeeApiLogs). MIGRATION_NOT_EXECUTED.
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys.
-- Why: Tingee support asks for the exact request behind an error (e.g. 400 "Verify string error ... Refid=").
-- TAPortal/Customer write one row per call (headers x-client-id/x-request-timestamp/x-signature, body, Tingee's answer)
-- under a LogCode shown with each error; Admin page /tingeelogs looks it up. The secret key is never stored.
-- Creates a new table only; changes no existing object or row. Idempotent.
-- Verify: database/scripts/017-verify-tingee-api-logs.sql. Rollback: database/scripts/018-rollback-tingee-api-logs.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
-- Equivalent required positive guard: DB_NAME() = 'TAPortalSys'. No USE statement.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.TingeeApiLogs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TingeeApiLogs (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_TingeeApiLogs PRIMARY KEY,
        LogCode varchar(32) NOT NULL,
        Method varchar(10) NOT NULL,
        Url nvarchar(1000) NOT NULL,
        ClientId nvarchar(200) NULL,
        RequestTimestamp varchar(50) NULL,
        Signature varchar(300) NULL,
        RequestBody nvarchar(max) NULL,
        HttpStatus int NULL,
        ResponseCode varchar(50) NULL,
        ResponseMessage nvarchar(2000) NULL,
        ResponseBody nvarchar(max) NULL,
        Error nvarchar(2000) NULL,
        DurationMs int NOT NULL,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_TAL_CreatedAt DEFAULT SYSUTCDATETIME()
    );
    CREATE UNIQUE INDEX UX_TAL_LogCode ON dbo.TingeeApiLogs(LogCode);
    PRINT N'dbo.TingeeApiLogs created';
END ELSE PRINT N'dbo.TingeeApiLogs exists';
COMMIT;
PRINT N'014-tingee-api-logs.sql: OK';
