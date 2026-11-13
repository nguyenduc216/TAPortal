-- ZALO-MINI-APP - sign-in to the T.A Zalo Mini App with the Zalo phone number (operator decisions 2026-11-13).
-- MIGRATION_NOT_EXECUTED.
-- Change: ZALO-MINI-APP-CHG-20261113T053124Z-A0CC3C.
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, after 037.
--   dbo.UserVerifiedPhones      the phone number a user proved is theirs (ZNS OTP on the web, or the Zalo-verified number
--                               when it matched the account in the Mini App). Only a verified phone opens the Mini App.
--   dbo.PhoneOtpChallenges      one OTP sent by ZNS: HMAC of the code only (never the code), expiry, attempts, consumed.
--   dbo.ZaloMiniAppQrTokens     one-time QR from TA Portal / Hoa don tu dong: SHA-256 of the token only, 5-minute expiry.
--   dbo.ZaloMiniAppLinks        a Zalo user (Mini App user id) linked to a T.A account after a successful sign-in.
-- New tables only; no existing row or column is changed. Idempotent.
-- Verify: database/scripts/065-verify-zalo-mini-app-auth.sql. Rollback: database/scripts/066-rollback-zalo-mini-app-auth.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
 THROW 51001, 'Precondition failed: dbo.Users missing (run 002 first)', 1;

IF OBJECT_ID(N'dbo.UserVerifiedPhones', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserVerifiedPhones (
        UserId uniqueidentifier NOT NULL CONSTRAINT PK_UserVerifiedPhones PRIMARY KEY,
        Phone varchar(15) NOT NULL,
        Method varchar(20) NOT NULL,
        VerifiedAt datetime2(3) NOT NULL CONSTRAINT DF_UVP_VerifiedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_UVP_User FOREIGN KEY (UserId) REFERENCES dbo.Users(Id),
        CONSTRAINT CK_UVP_Phone CHECK (Phone LIKE '84[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]'),
        CONSTRAINT CK_UVP_Method CHECK (Method IN ('ZNS_OTP','ZALO_MINIAPP'))
    );
    EXEC(N'CREATE INDEX IX_UserVerifiedPhones_Phone ON dbo.UserVerifiedPhones(Phone);');
    PRINT N'dbo.UserVerifiedPhones: created';
END ELSE PRINT N'dbo.UserVerifiedPhones: exists';

IF OBJECT_ID(N'dbo.PhoneOtpChallenges', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PhoneOtpChallenges (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_PhoneOtpChallenges PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        UserId uniqueidentifier NOT NULL,
        Phone varchar(15) NOT NULL,
        Purpose varchar(20) NOT NULL,
        CodeHash varbinary(32) NOT NULL,
        Attempts int NOT NULL CONSTRAINT DF_POC_Attempts DEFAULT 0,
        ExpiresAt datetime2(3) NOT NULL,
        ConsumedAt datetime2(3) NULL,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_POC_CreatedAt DEFAULT SYSUTCDATETIME(),
        RequestIp varchar(45) NULL,
        CONSTRAINT FK_POC_User FOREIGN KEY (UserId) REFERENCES dbo.Users(Id),
        CONSTRAINT CK_POC_Purpose CHECK (Purpose IN ('VERIFY_PHONE')),
        CONSTRAINT CK_POC_Attempts CHECK (Attempts BETWEEN 0 AND 10)
    );
    EXEC(N'CREATE INDEX IX_PhoneOtpChallenges_User ON dbo.PhoneOtpChallenges(UserId, CreatedAt);');
    PRINT N'dbo.PhoneOtpChallenges: created';
END ELSE PRINT N'dbo.PhoneOtpChallenges: exists';

IF OBJECT_ID(N'dbo.ZaloMiniAppQrTokens', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ZaloMiniAppQrTokens (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_ZaloMiniAppQrTokens PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        TokenHash varbinary(32) NOT NULL,
        UserId uniqueidentifier NOT NULL,
        Portal varchar(10) NOT NULL,
        ExpiresAt datetime2(3) NOT NULL,
        ConsumedAt datetime2(3) NULL,
        ConsumedByZaloUserId varchar(64) NULL,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_ZQT_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_ZQT_User FOREIGN KEY (UserId) REFERENCES dbo.Users(Id),
        CONSTRAINT CK_ZQT_Portal CHECK (Portal IN ('PORTAL','INVOICE'))
    );
    EXEC(N'CREATE UNIQUE INDEX UX_ZaloMiniAppQrTokens_Hash ON dbo.ZaloMiniAppQrTokens(TokenHash);');
    PRINT N'dbo.ZaloMiniAppQrTokens: created';
END ELSE PRINT N'dbo.ZaloMiniAppQrTokens: exists';

IF OBJECT_ID(N'dbo.ZaloMiniAppLinks', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ZaloMiniAppLinks (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_ZaloMiniAppLinks PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        ZaloUserId varchar(64) NOT NULL,
        UserId uniqueidentifier NOT NULL,
        Phone varchar(15) NOT NULL,
        Source varchar(10) NOT NULL,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_ZML_CreatedAt DEFAULT SYSUTCDATETIME(),
        LastUsedAt datetime2(3) NULL,
        RevokedAt datetime2(3) NULL,
        RevokedBy uniqueidentifier NULL,
        CONSTRAINT FK_ZML_User FOREIGN KEY (UserId) REFERENCES dbo.Users(Id),
        CONSTRAINT CK_ZML_Source CHECK (Source IN ('QR','LOGIN'))
    );
    EXEC(N'CREATE UNIQUE INDEX UX_ZaloMiniAppLinks_Active ON dbo.ZaloMiniAppLinks(ZaloUserId, UserId) WHERE RevokedAt IS NULL;');
    EXEC(N'CREATE INDEX IX_ZaloMiniAppLinks_User ON dbo.ZaloMiniAppLinks(UserId);');
    PRINT N'dbo.ZaloMiniAppLinks: created';
END ELSE PRINT N'dbo.ZaloMiniAppLinks: exists';

COMMIT TRANSACTION;
PRINT N'038-zalo-mini-app-auth: done';
