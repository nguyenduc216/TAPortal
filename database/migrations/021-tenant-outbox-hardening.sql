/* TAPortal 021 - Multi-tenant scope hardening and invoice/payment invariants. */
USE [TAPortal];
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.Companies') AND name='IX_Companies_Partner') CREATE INDEX IX_Companies_Partner ON dbo.Companies(PartnerId) WHERE PartnerId IS NOT NULL AND IsDeleted=0;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.Customers') AND name='IX_Customers_Partner') CREATE INDEX IX_Customers_Partner ON dbo.Customers(PartnerId,Name) WHERE PartnerId IS NOT NULL AND IsDeleted=0;
GO
DECLARE @sql nvarchar(max), @constraint sysname;
SELECT TOP 1 @constraint=cc.name FROM sys.check_constraints cc WHERE cc.parent_object_id=OBJECT_ID(N'dbo.Settings') AND cc.definition LIKE '%ScopeType%';
IF @constraint IS NOT NULL BEGIN SET @sql=N'ALTER TABLE dbo.Settings DROP CONSTRAINT '+QUOTENAME(@constraint); EXEC sp_executesql @sql; END;
ALTER TABLE dbo.Settings WITH CHECK ADD CONSTRAINT CK_Settings_ScopeType CHECK(ScopeType IN('SYSTEM','PARTNER','COMPANY','BRANCH','TEAM','USER'));
GO
DECLARE @sql nvarchar(max), @constraint sysname;
SELECT TOP 1 @constraint=cc.name FROM sys.check_constraints cc WHERE cc.parent_object_id=OBJECT_ID(N'dbo.NumberSequences') AND cc.definition LIKE '%Scope%';
IF @constraint IS NOT NULL BEGIN SET @sql=N'ALTER TABLE dbo.NumberSequences DROP CONSTRAINT '+QUOTENAME(@constraint); EXEC sp_executesql @sql; END;
ALTER TABLE dbo.NumberSequences WITH CHECK ADD CONSTRAINT CK_NumberSequences_Scope CHECK(Scope IN('SYSTEM','PARTNER','COMPANY','BRANCH','TEAM'));
GO
DECLARE @sql nvarchar(max), @constraint sysname;
SELECT TOP 1 @constraint=cc.name FROM sys.check_constraints cc WHERE cc.parent_object_id=OBJECT_ID(N'dbo.DataScopes') AND cc.definition LIKE '%ScopeType%';
IF @constraint IS NOT NULL BEGIN SET @sql=N'ALTER TABLE dbo.DataScopes DROP CONSTRAINT '+QUOTENAME(@constraint); EXEC sp_executesql @sql; END;
ALTER TABLE dbo.DataScopes WITH CHECK ADD CONSTRAINT CK_DataScopes_ScopeType CHECK(ScopeType IN('CUSTOM','PARTNER','COMPANY','BRANCH','TEAM','ASSIGNED','SELF'));
GO
IF NOT EXISTS(SELECT 1 FROM dbo.DataScopes WHERE Code='PARTNER') INSERT dbo.DataScopes(Code,Name,ScopeType,Description,IsActive) VALUES('PARTNER',N'Đối tác hiện tại','PARTNER',N'Dữ liệu thuộc Partner/Tenant hiện tại',1);
GO
IF OBJECT_ID(N'dbo.OutboxMessages',N'U') IS NULL BEGIN
 CREATE TABLE dbo.OutboxMessages(
  Id uniqueidentifier NOT NULL CONSTRAINT PK_OutboxMessages PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
  PartnerId uniqueidentifier NULL,AggregateType varchar(100) NOT NULL,AggregateId nvarchar(200) NOT NULL,
  EventType varchar(150) NOT NULL,Payload nvarchar(max) NOT NULL,Status varchar(20) NOT NULL DEFAULT 'PENDING',
  AttemptCount int NOT NULL DEFAULT 0,NextAttemptAt datetime2(3) NULL,LastError nvarchar(2000) NULL,
  CreatedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),ProcessedAt datetime2(3) NULL,
  CONSTRAINT FK_Outbox_Partner FOREIGN KEY(PartnerId) REFERENCES dbo.Partners(Id),
  CONSTRAINT CK_Outbox_Status CHECK(Status IN('PENDING','PROCESSING','PROCESSED','FAILED','DEAD'))
 );
 CREATE INDEX IX_Outbox_Dispatch ON dbo.OutboxMessages(Status,NextAttemptAt,CreatedAt) INCLUDE(EventType,AttemptCount);
END
GO
IF OBJECT_ID(N'dbo.IdempotencyKeys',N'U') IS NULL BEGIN
 CREATE TABLE dbo.IdempotencyKeys(
  Id uniqueidentifier NOT NULL CONSTRAINT PK_IdempotencyKeys PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
  PartnerId uniqueidentifier NULL,Scope varchar(100) NOT NULL,[Key] nvarchar(250) NOT NULL,
  ResourceType varchar(100) NULL,ResourceId nvarchar(200) NULL,CreatedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),ExpiresAt datetime2(3) NULL,
  CONSTRAINT FK_Idem_Partner FOREIGN KEY(PartnerId) REFERENCES dbo.Partners(Id)
 );
 CREATE UNIQUE INDEX UX_Idempotency_ScopeKey ON dbo.IdempotencyKeys(PartnerId,Scope,[Key]);
END
GO
PRINT '021-tenant-outbox-hardening.sql: OK';
GO