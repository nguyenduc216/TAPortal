/* TAPortal - Common database bootstrap 001
   SQL Server / idempotent
   NOTE: do not use [sys] for application objects; [sys] is SQL Server's system schema.
*/
USE [TAPortal];
GO

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'auth') EXEC(N'CREATE SCHEMA [auth]');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'org') EXEC(N'CREATE SCHEMA [org]');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'core') EXEC(N'CREATE SCHEMA [core]');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'crm') EXEC(N'CREATE SCHEMA [crm]');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'audit') EXEC(N'CREATE SCHEMA [audit]');
GO
