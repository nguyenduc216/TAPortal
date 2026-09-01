/*
  TAPortal - MCP read-only SQL principal
  IMPORTANT:
  - Replace the password before running.
  - Prefer executing this manually as a SQL administrator.
  - Do not commit a real password into this file.
*/

USE [master];
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'taportal_ai_reader')
BEGIN
    CREATE LOGIN [taportal_ai_reader]
    WITH PASSWORD = N'REPLACE_WITH_STRONG_PASSWORD',
         CHECK_POLICY = ON,
         CHECK_EXPIRATION = ON;
END
GO

USE [TAPortal];
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'taportal_ai_reader')
BEGIN
    CREATE USER [taportal_ai_reader] FOR LOGIN [taportal_ai_reader];
END
GO

ALTER ROLE [db_datareader] ADD MEMBER [taportal_ai_reader];
GRANT VIEW DEFINITION TO [taportal_ai_reader];
DENY INSERT TO [taportal_ai_reader];
DENY UPDATE TO [taportal_ai_reader];
DENY DELETE TO [taportal_ai_reader];
DENY EXECUTE TO [taportal_ai_reader];
GO

-- Verification
SELECT
    dp.name AS DatabaseUser,
    rp.name AS DatabaseRole
FROM sys.database_role_members drm
JOIN sys.database_principals rp ON rp.principal_id = drm.role_principal_id
JOIN sys.database_principals dp ON dp.principal_id = drm.member_principal_id
WHERE dp.name = N'taportal_ai_reader';
GO
