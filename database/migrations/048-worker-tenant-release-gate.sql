/* V1 RC4 048 - tenant graph + worker lifecycle release gate. */
USE [TAPortal];
GO
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.CustomerCashAccounts') AND name='UX_CCA_PartnerId_Id') CREATE UNIQUE INDEX UX_CCA_PartnerId_Id ON dbo.CustomerCashAccounts(PartnerId,Id);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.CreditReservations') AND name='UX_CR_PartnerId_Id') CREATE UNIQUE INDEX UX_CR_PartnerId_Id ON dbo.CreditReservations(PartnerId,Id);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.InvoiceRequests') AND name='UX_IReq_PartnerId_Id') CREATE UNIQUE INDEX UX_IReq_PartnerId_Id ON dbo.InvoiceRequests(PartnerId,Id);
GO
IF COL_LENGTH('dbo.InvoiceCreditConsumptions','ReservationPartnerId') IS NULL ALTER TABLE dbo.InvoiceCreditConsumptions ADD ReservationPartnerId uniqueidentifier NULL;
GO
UPDATE dbo.InvoiceCreditConsumptions SET ReservationPartnerId=PartnerId WHERE ReservationPartnerId IS NULL;
ALTER TABLE dbo.InvoiceCreditConsumptions ALTER COLUMN ReservationPartnerId uniqueidentifier NOT NULL;
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name='FK_ICC_TenantReservation') ALTER TABLE dbo.InvoiceCreditConsumptions ADD CONSTRAINT FK_ICC_TenantReservation FOREIGN KEY(ReservationPartnerId,CreditReservationId) REFERENCES dbo.CreditReservations(PartnerId,Id);
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name='CK_ICC_ReservationPartner') ALTER TABLE dbo.InvoiceCreditConsumptions ADD CONSTRAINT CK_ICC_ReservationPartner CHECK(ReservationPartnerId=PartnerId);
GO
IF OBJECT_ID(N'dbo.sp_CompleteInboxMessage',N'P') IS NULL EXEC('CREATE PROCEDURE dbo.sp_CompleteInboxMessage AS BEGIN SET NOCOUNT ON; END');
GO
ALTER PROCEDURE dbo.sp_CompleteInboxMessage @Id uniqueidentifier,@LockId uniqueidentifier
AS BEGIN SET NOCOUNT ON; UPDATE dbo.InboxMessages SET Status='PROCESSED',ProcessedAt=SYSUTCDATETIME(),LockedUntil=NULL,LockId=NULL,LastError=NULL WHERE Id=@Id AND Status='PROCESSING' AND LockId=@LockId; IF @@ROWCOUNT=0 THROW 51630,'Inbox lease not owned.',1; END
GO
IF OBJECT_ID(N'dbo.sp_FailInboxMessage',N'P') IS NULL EXEC('CREATE PROCEDURE dbo.sp_FailInboxMessage AS BEGIN SET NOCOUNT ON; END');
GO
ALTER PROCEDURE dbo.sp_FailInboxMessage @Id uniqueidentifier,@LockId uniqueidentifier,@Error nvarchar(2000)
AS BEGIN SET NOCOUNT ON; UPDATE dbo.InboxMessages SET Status=CASE WHEN AttemptCount>=MaxAttempts THEN 'DEAD' ELSE 'FAILED' END,LastError=@Error,NextAttemptAt=CASE WHEN AttemptCount>=MaxAttempts THEN NULL ELSE DATEADD(MINUTE,CASE WHEN AttemptCount<8 THEN AttemptCount ELSE 8 END,SYSUTCDATETIME()) END,LockedUntil=NULL,LockId=NULL WHERE Id=@Id AND Status='PROCESSING' AND LockId=@LockId; IF @@ROWCOUNT=0 THROW 51631,'Inbox lease not owned.',1; END
GO
IF OBJECT_ID(N'dbo.sp_RenewInboxLease',N'P') IS NULL EXEC('CREATE PROCEDURE dbo.sp_RenewInboxLease AS BEGIN SET NOCOUNT ON; END');
GO
ALTER PROCEDURE dbo.sp_RenewInboxLease @Id uniqueidentifier,@LockId uniqueidentifier,@LeaseSeconds int=60
AS BEGIN SET NOCOUNT ON; UPDATE dbo.InboxMessages SET LockedUntil=DATEADD(SECOND,@LeaseSeconds,SYSUTCDATETIME()) WHERE Id=@Id AND Status='PROCESSING' AND LockId=@LockId; IF @@ROWCOUNT=0 THROW 51632,'Inbox lease not owned.',1; END
GO
IF COL_LENGTH('dbo.OutboxMessages','LockId') IS NULL ALTER TABLE dbo.OutboxMessages ADD LockId uniqueidentifier NULL;
IF COL_LENGTH('dbo.OutboxMessages','LockedUntil') IS NULL ALTER TABLE dbo.OutboxMessages ADD LockedUntil datetime2(3) NULL;
GO
IF OBJECT_ID(N'dbo.sp_ClaimOutboxMessage',N'P') IS NULL EXEC('CREATE PROCEDURE dbo.sp_ClaimOutboxMessage AS BEGIN SET NOCOUNT ON; END');
GO
ALTER PROCEDURE dbo.sp_ClaimOutboxMessage @LeaseSeconds int=60
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON; DECLARE @lock uniqueidentifier=NEWID(),@id uniqueidentifier;
 BEGIN TRY BEGIN TRAN;
  SELECT TOP 1 @id=Id FROM dbo.OutboxMessages WITH(UPDLOCK,READPAST,ROWLOCK) WHERE Status IN('PENDING','FAILED') AND AttemptCount<MaxAttempts AND (NextAttemptAt IS NULL OR NextAttemptAt<=SYSUTCDATETIME()) AND (LockedUntil IS NULL OR LockedUntil<SYSUTCDATETIME()) ORDER BY CreatedAt;
  IF @id IS NOT NULL UPDATE dbo.OutboxMessages SET Status='PROCESSING',AttemptCount=AttemptCount+1,LockId=@lock,LockedUntil=DATEADD(SECOND,@LeaseSeconds,SYSUTCDATETIME()) WHERE Id=@id;
  COMMIT; SELECT * FROM dbo.OutboxMessages WHERE Id=@id AND LockId=@lock;
 END TRY BEGIN CATCH IF XACT_STATE()<>0 ROLLBACK; THROW; END CATCH
END
GO
IF OBJECT_ID(N'dbo.sp_CompleteOutboxMessage',N'P') IS NULL EXEC('CREATE PROCEDURE dbo.sp_CompleteOutboxMessage AS BEGIN SET NOCOUNT ON; END');
GO
ALTER PROCEDURE dbo.sp_CompleteOutboxMessage @Id uniqueidentifier,@LockId uniqueidentifier
AS BEGIN SET NOCOUNT ON; UPDATE dbo.OutboxMessages SET Status='PROCESSED',ProcessedAt=SYSUTCDATETIME(),LockedUntil=NULL,LockId=NULL,LastError=NULL WHERE Id=@Id AND Status='PROCESSING' AND LockId=@LockId; IF @@ROWCOUNT=0 THROW 51633,'Outbox lease not owned.',1; END
GO
IF OBJECT_ID(N'dbo.sp_FailOutboxMessage',N'P') IS NULL EXEC('CREATE PROCEDURE dbo.sp_FailOutboxMessage AS BEGIN SET NOCOUNT ON; END');
GO
ALTER PROCEDURE dbo.sp_FailOutboxMessage @Id uniqueidentifier,@LockId uniqueidentifier,@Error nvarchar(2000)
AS BEGIN SET NOCOUNT ON; UPDATE dbo.OutboxMessages SET Status=CASE WHEN AttemptCount>=MaxAttempts THEN 'DEAD' ELSE 'FAILED' END,LastError=@Error,NextAttemptAt=CASE WHEN AttemptCount>=MaxAttempts THEN NULL ELSE DATEADD(MINUTE,CASE WHEN AttemptCount<8 THEN AttemptCount ELSE 8 END,SYSUTCDATETIME()) END,LockedUntil=NULL,LockId=NULL WHERE Id=@Id AND Status='PROCESSING' AND LockId=@LockId; IF @@ROWCOUNT=0 THROW 51634,'Outbox lease not owned.',1; END
GO
PRINT '048-worker-tenant-release-gate.sql: OK';
GO