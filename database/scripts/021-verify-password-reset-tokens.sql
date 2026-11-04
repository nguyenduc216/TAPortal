-- CUSTOMER-PORTAL-FOUNDATION-CHG-20261104T064318Z-148859 — READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/016-password-reset-tokens.sql. Expected last line: VERIFY_STATUS=PASS.
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
DECLARE @fail int = 0;
PRINT N'=== PASSWORD RESET TOKENS VERIFY ===';
IF OBJECT_ID(N'dbo.PasswordResetTokens', N'U') IS NULL BEGIN PRINT N'FAIL: dbo.PasswordResetTokens missing'; SET @fail += 1; END
ELSE
BEGIN
    PRINT N'PASS: dbo.PasswordResetTokens';
    IF (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.PasswordResetTokens') AND name IN (N'Id',N'UserId',N'TokenHash',N'CreatedAt',N'ExpiresAt',N'UsedAt',N'RequestIp')) = 7
        PRINT N'PASS: 7 columns';
    ELSE BEGIN PRINT N'FAIL: columns missing'; SET @fail += 1; END
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.PasswordResetTokens') AND name = N'UX_PRT_TokenHash' AND is_unique = 1 AND is_disabled = 0)
        PRINT N'PASS: UX_PRT_TokenHash';
    ELSE BEGIN PRINT N'FAIL: unique index UX_PRT_TokenHash missing/disabled'; SET @fail += 1; END
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'dbo.PasswordResetTokens') AND name = N'FK_PRT_User')
        PRINT N'PASS: FK_PRT_User';
    ELSE BEGIN PRINT N'FAIL: FK_PRT_User missing'; SET @fail += 1; END
END
PRINT N'VERIFY_STATUS=' + CASE WHEN @fail = 0 THEN N'PASS' ELSE N'FAIL' END;
