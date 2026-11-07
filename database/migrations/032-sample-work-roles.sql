-- CONTRACTS-AND-TASKS - two sample roles for the work and contract modules (operator request 2026-11-07). MIGRATION_NOT_EXECUTED.
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, after 031.
--   STAFF   "Nhan vien": TASKS.VIEW, TASKS.CREATE
--   MANAGER "Quan ly"  : every TASKS.* and CONTRACTS.* permission of migration 031
-- New rows only (dbo.Roles, dbo.RolePermissions marked CreatedBy = A0000032-0000-4000-8000-000000000032); a role whose code
-- already exists is left untouched. Roles stay editable on "Vai tro & phan quyen" (SYS_ADMIN). Names are written with NCHAR
-- because sqlcmd reads the file in the server code page.
-- Verify: database/scripts/053-verify-sample-work-roles.sql. Rollback: database/scripts/054-rollback-sample-work-roles.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @Marker uniqueidentifier = 'A0000032-0000-4000-8000-000000000032';
IF (SELECT COUNT(1) FROM dbo.Permissions WHERE IsDeleted = 0 AND Code IN ('TASKS.VIEW','TASKS.CREATE','TASKS.ASSIGN','TASKS.VIEW_ALL','TASKS.TEMPLATES.MANAGE',
    'TASKS.REPORT.VIEW','CONTRACTS.VIEW','CONTRACTS.CREATE','CONTRACTS.UPDATE','CONTRACTS.DELETE')) <> 10
 THROW 51001, 'Precondition failed: permissions of migration 031 missing', 1;
DECLARE @Roles TABLE (Code varchar(50) COLLATE DATABASE_DEFAULT NOT NULL PRIMARY KEY, Name nvarchar(200) NOT NULL, Description nvarchar(500) NOT NULL);
INSERT @Roles (Code, Name, Description) VALUES
 ('STAFF', N'Nh' + NCHAR(226) + N'n vi' + NCHAR(234) + N'n',
  N'Xem v' + NCHAR(224) + N' c' + NCHAR(7853) + N'p nh' + NCHAR(7853) + N't vi' + NCHAR(7879) + N'c c' + NCHAR(7911) + N'a m' + NCHAR(236) + N'nh, t' + NCHAR(7921) + N' t' + NCHAR(7841) + N'o vi' + NCHAR(7879) + N'c cho m' + NCHAR(236) + N'nh (m' + NCHAR(7851) + N'u t' + NCHAR(7915) + N' migration 032)'),
 ('MANAGER', N'Qu' + NCHAR(7843) + N'n l' + NCHAR(253),
  N'Giao vi' + NCHAR(7879) + N'c, gi' + NCHAR(225) + N'm s' + NCHAR(225) + N't m' + NCHAR(7885) + N'i c' + NCHAR(244) + N'ng vi' + NCHAR(7879) + N'c, vi' + NCHAR(7879) + N'c l' + NCHAR(7863) + N'p, b' + NCHAR(225) + N'o c' + NCHAR(225) + N'o ti' + NCHAR(7871) + N'n ' + NCHAR(273) + NCHAR(7897) + N' v' + NCHAR(224) + N' qu' + NCHAR(7843) + N'n l' + NCHAR(253) + N' h' + NCHAR(7907) + N'p ' + NCHAR(273) + NCHAR(7891) + N'ng (m' + NCHAR(7851) + N'u t' + NCHAR(7915) + N' migration 032)');
DECLARE @Grants TABLE (RoleCode varchar(50) COLLATE DATABASE_DEFAULT NOT NULL, PermissionCode varchar(200) COLLATE DATABASE_DEFAULT NOT NULL, PRIMARY KEY (RoleCode, PermissionCode));
INSERT @Grants (RoleCode, PermissionCode) VALUES
 ('STAFF', 'TASKS.VIEW'), ('STAFF', 'TASKS.CREATE'),
 ('MANAGER', 'TASKS.VIEW'), ('MANAGER', 'TASKS.CREATE'), ('MANAGER', 'TASKS.ASSIGN'), ('MANAGER', 'TASKS.VIEW_ALL'), ('MANAGER', 'TASKS.TEMPLATES.MANAGE'),
 ('MANAGER', 'TASKS.REPORT.VIEW'), ('MANAGER', 'CONTRACTS.VIEW'), ('MANAGER', 'CONTRACTS.CREATE'), ('MANAGER', 'CONTRACTS.UPDATE'), ('MANAGER', 'CONTRACTS.DELETE');
DECLARE @New TABLE (Code varchar(50) COLLATE DATABASE_DEFAULT NOT NULL PRIMARY KEY);
INSERT @New (Code) SELECT r.Code FROM @Roles r WHERE NOT EXISTS (SELECT 1 FROM dbo.Roles x WHERE x.Code = r.Code);
INSERT dbo.Roles (Id, Code, Name, Description, IsSystem, IsActive, CreatedBy)
SELECT NEWID(), r.Code, r.Name, r.Description, 0, 1, @Marker FROM @Roles r JOIN @New n ON n.Code = r.Code;
INSERT dbo.RolePermissions (RoleId, PermissionId, CreatedBy)
SELECT ro.Id, p.Id, @Marker
FROM @Grants g JOIN @New n ON n.Code = g.RoleCode
JOIN dbo.Roles ro ON ro.Code = g.RoleCode AND ro.CreatedBy = @Marker
JOIN dbo.Permissions p ON p.Code = g.PermissionCode AND p.IsDeleted = 0;
COMMIT;
PRINT N'032-sample-work-roles.sql: OK';
