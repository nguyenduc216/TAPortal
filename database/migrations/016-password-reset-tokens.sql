-- CUSTOMER-PORTAL-FOUNDATION-CHG-20261104T064318Z-148859 — dbo.PasswordResetTokens for Customer Portal "Quên mật khẩu". MIGRATION_NOT_EXECUTED.
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys.
-- One row per emailed reset link: only the SHA-256 of the random token is stored (the token itself is only in the email),
-- valid 30 minutes, usable once; a new request supersedes older unused links. Creates a new table only. Idempotent.
-- Verify: database/scripts/021-verify-password-reset-tokens.sql. Rollback: database/scripts/022-rollback-password-reset-tokens.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
-- Equivalent required positive guard: DB_NAME() = 'TAPortalSys'. No USE statement.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
 THROW 51001, 'Precondition failed: dbo.Users missing', 1;
IF OBJECT_ID(N'dbo.PasswordResetTokens', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PasswordResetTokens (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_PasswordResetTokens PRIMARY KEY,
        UserId uniqueidentifier NOT NULL CONSTRAINT FK_PRT_User REFERENCES dbo.Users(Id),
        TokenHash binary(32) NOT NULL,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_PRT_CreatedAt DEFAULT SYSUTCDATETIME(),
        ExpiresAt datetime2(3) NOT NULL,
        UsedAt datetime2(3) NULL,
        RequestIp varchar(45) NULL
    );
    CREATE UNIQUE INDEX UX_PRT_TokenHash ON dbo.PasswordResetTokens(TokenHash);
    CREATE INDEX IX_PRT_User_Created ON dbo.PasswordResetTokens(UserId, CreatedAt);
    PRINT N'dbo.PasswordResetTokens created';
END ELSE PRINT N'dbo.PasswordResetTokens exists';
COMMIT;
PRINT N'016-password-reset-tokens.sql: OK';
