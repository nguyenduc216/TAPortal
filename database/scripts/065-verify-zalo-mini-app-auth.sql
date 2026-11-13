-- ZALO-MINI-APP - READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/038-zalo-mini-app-auth.sql. Expected last line: VERIFY_STATUS=PASS (NOT_APPLIED before the migration).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
DECLARE @fail int = 0;
DECLARE @phones bit = CASE WHEN OBJECT_ID(N'dbo.UserVerifiedPhones', N'U') IS NULL THEN 0 ELSE 1 END;
DECLARE @otp bit = CASE WHEN OBJECT_ID(N'dbo.PhoneOtpChallenges', N'U') IS NULL THEN 0 ELSE 1 END;
DECLARE @qr bit = CASE WHEN OBJECT_ID(N'dbo.ZaloMiniAppQrTokens', N'U') IS NULL THEN 0 ELSE 1 END;
DECLARE @links bit = CASE WHEN OBJECT_ID(N'dbo.ZaloMiniAppLinks', N'U') IS NULL THEN 0 ELSE 1 END;
PRINT N'=== ZALO MINI APP AUTH VERIFY ===';
IF @phones = 0 AND @otp = 0 AND @qr = 0 AND @links = 0
BEGIN
    PRINT N'INFO: dbo.UserVerifiedPhones, dbo.PhoneOtpChallenges, dbo.ZaloMiniAppQrTokens and dbo.ZaloMiniAppLinks missing';
    PRINT N'VERIFY_STATUS=NOT_APPLIED';
    RETURN;
END
IF @phones = 1 PRINT N'PASS: dbo.UserVerifiedPhones'; ELSE BEGIN PRINT N'FAIL: dbo.UserVerifiedPhones missing'; SET @fail += 1; END
IF @otp = 1 PRINT N'PASS: dbo.PhoneOtpChallenges'; ELSE BEGIN PRINT N'FAIL: dbo.PhoneOtpChallenges missing'; SET @fail += 1; END
IF @qr = 1 PRINT N'PASS: dbo.ZaloMiniAppQrTokens'; ELSE BEGIN PRINT N'FAIL: dbo.ZaloMiniAppQrTokens missing'; SET @fail += 1; END
IF @links = 1 PRINT N'PASS: dbo.ZaloMiniAppLinks'; ELSE BEGIN PRINT N'FAIL: dbo.ZaloMiniAppLinks missing'; SET @fail += 1; END
IF (SELECT COUNT(*) FROM sys.check_constraints WHERE is_disabled = 0 AND name IN (N'CK_UVP_Phone', N'CK_UVP_Method', N'CK_POC_Purpose', N'CK_POC_Attempts', N'CK_ZQT_Portal', N'CK_ZML_Source')) = 6
    PRINT N'PASS: 6 check constraints'; ELSE BEGIN PRINT N'FAIL: a check constraint is missing'; SET @fail += 1; END
IF (SELECT COUNT(*) FROM sys.foreign_keys WHERE name IN (N'FK_UVP_User', N'FK_POC_User', N'FK_ZQT_User', N'FK_ZML_User')) = 4
    PRINT N'PASS: 4 foreign keys'; ELSE BEGIN PRINT N'FAIL: a foreign key is missing'; SET @fail += 1; END
IF (SELECT COUNT(*) FROM sys.indexes WHERE name IN (N'IX_UserVerifiedPhones_Phone', N'IX_PhoneOtpChallenges_User', N'UX_ZaloMiniAppQrTokens_Hash', N'UX_ZaloMiniAppLinks_Active', N'IX_ZaloMiniAppLinks_User')) = 5
    PRINT N'PASS: 5 indexes'; ELSE BEGIN PRINT N'FAIL: an index is missing'; SET @fail += 1; END
PRINT N'VERIFY_STATUS=' + CASE WHEN @fail = 0 THEN N'PASS' ELSE N'FAIL' END;
