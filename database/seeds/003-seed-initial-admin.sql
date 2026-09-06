/* TAPortal - Initial administrator seed
   Creates the bootstrap admin account if it does not exist.
   Password: Admin@123
   IMPORTANT: change the password immediately after first login.
   PasswordHash uses TAPortal.Web PBKDF2-SHA256 format.
*/
USE [TAPortal];
GO

DECLARE @AdminRole uniqueidentifier = (SELECT TOP 1 Id FROM dbo.Roles WHERE Code='SYS_ADMIN' AND IsDeleted=0);

IF @AdminRole IS NULL
BEGIN
    INSERT dbo.Roles(Code,Name,Description,IsSystem,IsActive)
    VALUES('SYS_ADMIN',N'Quản trị hệ thống',N'Quản trị toàn bộ hệ thống',1,1);
    SET @AdminRole = (SELECT TOP 1 Id FROM dbo.Roles WHERE Code='SYS_ADMIN' AND IsDeleted=0);
END

DECLARE @AdminUser uniqueidentifier = (SELECT TOP 1 Id FROM dbo.Users WHERE NormalizedUsername='ADMIN' AND IsDeleted=0);

IF @AdminUser IS NULL
BEGIN
    SET @AdminUser = NEWID();
    INSERT dbo.Users(
        Id,Username,NormalizedUsername,Email,NormalizedEmail,DisplayName,
        PasswordHash,Status,IsActive,IsDeleted
    )
    VALUES(
        @AdminUser,
        'admin','ADMIN','admin@ta.local','ADMIN@TA.LOCAL',N'T.A Administrator',
        'PBKDF2$SHA256$150000$W/VCjeERevqOcWVTVYOPsA==$kfjc7OPGVPv2J2jmxfQ6kLno3TNfD0miHQ1tT74LDFs=',
        'ACTIVE',1,0
    );
END
ELSE
BEGIN
    UPDATE dbo.Users
    SET IsActive=1, Status='ACTIVE'
    WHERE Id=@AdminUser;
END

IF NOT EXISTS(SELECT 1 FROM dbo.UserRoles WHERE UserId=@AdminUser AND RoleId=@AdminRole)
    INSERT dbo.UserRoles(UserId,RoleId) VALUES(@AdminUser,@AdminRole);

INSERT dbo.RolePermissions(RoleId,PermissionId)
SELECT @AdminRole,p.Id
FROM dbo.Permissions p
WHERE p.IsDeleted=0
  AND NOT EXISTS(SELECT 1 FROM dbo.RolePermissions rp WHERE rp.RoleId=@AdminRole AND rp.PermissionId=p.Id);

PRINT '003-seed-initial-admin.sql: OK';
GO
