/* V1 RC3 044 - worker reliability/security hardening. */
USE [TAPortal];
GO
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO
IF COL_LENGTH('dbo.InboxMessages','MaxAttempts') IS NULL ALTER TABLE dbo.InboxMessages ADD MaxAttempts int NOT NULL CONSTRAINT DF_Inbox_MaxAttempts DEFAULT 7;
IF COL_LENGTH('dbo.InboxMessages','NextAttemptAt') IS NULL ALTER TABLE dbo.InboxMessages ADD NextAttemptAt datetime2(3) NULL;
IF COL_LENGTH('dbo.OutboxMessages','MaxAttempts') IS NULL ALTER TABLE dbo.OutboxMessages ADD MaxAttempts int NOT NULL CONSTRAINT DF_Outbox_MaxAttempts DEFAULT 7;
GO
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name='CK_Inbox_MaxAttempts') ALTER TABLE dbo.InboxMessages ADD CONSTRAINT CK_Inbox_MaxAttempts CHECK(MaxAttempts BETWEEN 1 AND 50);
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name='CK_Outbox_MaxAttempts') ALTER TABLE dbo.OutboxMessages ADD CONSTRAINT CK_Outbox_MaxAttempts CHECK(MaxAttempts BETWEEN 1 AND 50);
GO
IF OBJECT_ID(N'dbo.sp_ClaimInboxMessage',N'P') IS NULL EXEC('CREATE PROCEDURE dbo.sp_ClaimInboxMessage AS BEGIN SET NOCOUNT ON; END');
GO
ALTER PROCEDURE dbo.sp_ClaimInboxMessage @LeaseSeconds int=60
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON; DECLARE @lock uniqueidentifier=NEWID(),@id uniqueidentifier;
 BEGIN TRAN;
 SELECT TOP 1 @id=Id FROM dbo.InboxMessages WITH(UPDLOCK,READPAST,ROWLOCK) WHERE Status IN('RECEIVED','FAILED') AND AttemptCount<MaxAttempts AND (NextAttemptAt IS NULL OR NextAttemptAt<=SYSUTCDATETIME()) AND (LockedUntil IS NULL OR LockedUntil<SYSUTCDATETIME()) ORDER BY ReceivedAt;
 IF @id IS NOT NULL UPDATE dbo.InboxMessages SET Status='PROCESSING',AttemptCount=AttemptCount+1,LockId=@lock,LockedUntil=DATEADD(SECOND,@LeaseSeconds,SYSUTCDATETIME()) WHERE Id=@id;
 COMMIT; SELECT * FROM dbo.InboxMessages WHERE Id=@id AND LockId=@lock;
END
GO
IF OBJECT_ID(N'dbo.tr_BankLinkSession_NoHostedUrl',N'TR') IS NULL EXEC('CREATE TRIGGER dbo.tr_BankLinkSession_NoHostedUrl ON dbo.BankLinkSessions AFTER INSERT,UPDATE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM inserted WHERE HostedLinkUrl IS NOT NULL) THROW 51440,''HostedLinkUrl/token-bearing URL must not be persisted.'',1; END');
GO
PRINT '044-worker-security-preflight.sql: OK';
GO