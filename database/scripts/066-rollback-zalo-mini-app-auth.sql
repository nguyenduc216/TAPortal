-- ZALO-MINI-APP - rollback of migrations/038-zalo-mini-app-auth.sql.
-- Drops only the four tables 038 created (Mini App links, QR tokens, OTP challenges, verified phones); no other object.
-- After it, database/scripts/065-verify-zalo-mini-app-auth.sql must print VERIFY_STATUS=NOT_APPLIED.
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.ZaloMiniAppLinks', N'U') IS NOT NULL BEGIN DROP TABLE dbo.ZaloMiniAppLinks; PRINT N'dbo.ZaloMiniAppLinks: dropped'; END
IF OBJECT_ID(N'dbo.ZaloMiniAppQrTokens', N'U') IS NOT NULL BEGIN DROP TABLE dbo.ZaloMiniAppQrTokens; PRINT N'dbo.ZaloMiniAppQrTokens: dropped'; END
IF OBJECT_ID(N'dbo.PhoneOtpChallenges', N'U') IS NOT NULL BEGIN DROP TABLE dbo.PhoneOtpChallenges; PRINT N'dbo.PhoneOtpChallenges: dropped'; END
IF OBJECT_ID(N'dbo.UserVerifiedPhones', N'U') IS NOT NULL BEGIN DROP TABLE dbo.UserVerifiedPhones; PRINT N'dbo.UserVerifiedPhones: dropped'; END
COMMIT TRANSACTION;
PRINT N'066-rollback-zalo-mini-app-auth: done';
