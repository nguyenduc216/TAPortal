-- LEGAL-WATCH - rollback of migrations/048-legal-watch.sql.
-- Drops dbo.LegalWatchItems, dbo.LegalWatchRuns and the SourceUrl/EffectStatus/StatusCheckedAt columns of
-- dbo.LegalDocuments. Refuses (THROW 51002) while a queue entry has been decided by a person (IMPORTED, APPLIED,
-- DISMISSED). Documents already imported into the library stay there (they are ordinary dbo.LegalDocuments rows).
-- After it, database/scripts/085-verify-legal-watch.sql must print VERIFY_STATUS=NOT_APPLIED.
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @decided int = 0;
IF OBJECT_ID(N'dbo.LegalWatchItems', N'U') IS NOT NULL
    EXEC sp_executesql N'SELECT @n = COUNT(1) FROM dbo.LegalWatchItems WHERE Status <> ''PENDING'';', N'@n int OUTPUT', @n = @decided OUTPUT;
IF @decided > 0
 THROW 51002, 'Rollback refused: dbo.LegalWatchItems contains decided entries; no changes executed', 1;
IF OBJECT_ID(N'dbo.LegalWatchItems', N'U') IS NOT NULL BEGIN DROP TABLE dbo.LegalWatchItems; PRINT N'dbo.LegalWatchItems: dropped'; END
IF OBJECT_ID(N'dbo.LegalWatchRuns', N'U') IS NOT NULL BEGIN DROP TABLE dbo.LegalWatchRuns; PRINT N'dbo.LegalWatchRuns: dropped'; END
IF COL_LENGTH(N'dbo.LegalDocuments', N'SourceUrl') IS NOT NULL
BEGIN
    EXEC(N'ALTER TABLE dbo.LegalDocuments DROP COLUMN SourceUrl, EffectStatus, StatusCheckedAt;');
    PRINT N'dbo.LegalDocuments: SourceUrl, EffectStatus, StatusCheckedAt dropped';
END
COMMIT TRANSACTION;
PRINT N'086-rollback-legal-watch: done';
