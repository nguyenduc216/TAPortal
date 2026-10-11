/* V1 RC3 039 - deployment governance. SQL Server 2014 / compat 120. */
USE [TAPortal];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
IF OBJECT_ID(N'dbo.SchemaMigrationHistory',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.SchemaMigrationHistory(
  MigrationId varchar(20) NOT NULL CONSTRAINT PK_SchemaMigrationHistory PRIMARY KEY,
  ScriptName nvarchar(260) NOT NULL,
  ScriptHash varchar(64) NOT NULL,
  HashAlgorithm varchar(20) NOT NULL CONSTRAINT DF_SMH_Alg DEFAULT 'GIT_BLOB_SHA1',
  AppliedAt datetime2(3) NOT NULL CONSTRAINT DF_SMH_Applied DEFAULT SYSUTCDATETIME(),
  AppliedBy nvarchar(128) NULL,
  ReleaseVersion varchar(30) NULL,
  Succeeded bit NOT NULL,
  Notes nvarchar(1000) NULL
 );
END
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.SchemaMigrationHistory') AND name='UX_SMH_ScriptHash')
 CREATE UNIQUE INDEX UX_SMH_ScriptHash ON dbo.SchemaMigrationHistory(MigrationId,ScriptHash);
GO
PRINT '039-deployment-governance.sql: OK';
GO