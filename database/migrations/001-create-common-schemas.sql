/* TAPortal - Common database bootstrap 001
   SQL Server 2014+ compatible
   Standardize ALL application tables under [dbo].
   This script also transfers known legacy tables from auth/org/core/audit to dbo.
*/
USE [TAPortal];
GO

/* Move legacy AUTH tables to dbo, preserving data/keys/FKs. */
IF OBJECT_ID(N'auth.Users',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.Users',N'U') IS NULL ALTER SCHEMA dbo TRANSFER auth.Users;
IF OBJECT_ID(N'auth.Roles',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.Roles',N'U') IS NULL ALTER SCHEMA dbo TRANSFER auth.Roles;
IF OBJECT_ID(N'auth.Permissions',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.Permissions',N'U') IS NULL ALTER SCHEMA dbo TRANSFER auth.Permissions;
IF OBJECT_ID(N'auth.UserRoles',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.UserRoles',N'U') IS NULL ALTER SCHEMA dbo TRANSFER auth.UserRoles;
IF OBJECT_ID(N'auth.RolePermissions',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.RolePermissions',N'U') IS NULL ALTER SCHEMA dbo TRANSFER auth.RolePermissions;
IF OBJECT_ID(N'auth.UserPermissions',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.UserPermissions',N'U') IS NULL ALTER SCHEMA dbo TRANSFER auth.UserPermissions;
IF OBJECT_ID(N'auth.DataScopes',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.DataScopes',N'U') IS NULL ALTER SCHEMA dbo TRANSFER auth.DataScopes;
IF OBJECT_ID(N'auth.RoleDataScopes',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.RoleDataScopes',N'U') IS NULL ALTER SCHEMA dbo TRANSFER auth.RoleDataScopes;
IF OBJECT_ID(N'auth.UserDataScopes',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.UserDataScopes',N'U') IS NULL ALTER SCHEMA dbo TRANSFER auth.UserDataScopes;
GO

/* Move legacy ORG tables to dbo. */
IF OBJECT_ID(N'org.Companies',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.Companies',N'U') IS NULL ALTER SCHEMA dbo TRANSFER org.Companies;
IF OBJECT_ID(N'org.Branches',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.Branches',N'U') IS NULL ALTER SCHEMA dbo TRANSFER org.Branches;
IF OBJECT_ID(N'org.Teams',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.Teams',N'U') IS NULL ALTER SCHEMA dbo TRANSFER org.Teams;
IF OBJECT_ID(N'org.UserBranches',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.UserBranches',N'U') IS NULL ALTER SCHEMA dbo TRANSFER org.UserBranches;
IF OBJECT_ID(N'org.UserTeams',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.UserTeams',N'U') IS NULL ALTER SCHEMA dbo TRANSFER org.UserTeams;
GO

/* Move legacy CORE tables to dbo. */
IF OBJECT_ID(N'core.Modules',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.Modules',N'U') IS NULL ALTER SCHEMA dbo TRANSFER core.Modules;
IF OBJECT_ID(N'core.Functions',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.Functions',N'U') IS NULL ALTER SCHEMA dbo TRANSFER core.Functions;
IF OBJECT_ID(N'core.Menus',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.Menus',N'U') IS NULL ALTER SCHEMA dbo TRANSFER core.Menus;
IF OBJECT_ID(N'core.MenuPermissions',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.MenuPermissions',N'U') IS NULL ALTER SCHEMA dbo TRANSFER core.MenuPermissions;
IF OBJECT_ID(N'core.Settings',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.Settings',N'U') IS NULL ALTER SCHEMA dbo TRANSFER core.Settings;
IF OBJECT_ID(N'core.NumberSequences',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.NumberSequences',N'U') IS NULL ALTER SCHEMA dbo TRANSFER core.NumberSequences;
GO

/* Move legacy AUDIT tables to dbo. */
IF OBJECT_ID(N'audit.AuditLogs',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.AuditLogs',N'U') IS NULL ALTER SCHEMA dbo TRANSFER audit.AuditLogs;
IF OBJECT_ID(N'audit.LoginHistories',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.LoginHistories',N'U') IS NULL ALTER SCHEMA dbo TRANSFER audit.LoginHistories;
IF OBJECT_ID(N'audit.SystemLogs',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.SystemLogs',N'U') IS NULL ALTER SCHEMA dbo TRANSFER audit.SystemLogs;
GO

PRINT '001-create-common-schemas.sql: dbo standardization OK';
GO
