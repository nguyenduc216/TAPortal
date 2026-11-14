-- LEGAL-WATCH - READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/048-legal-watch.sql. Expected last line: VERIFY_STATUS=PASS (NOT_APPLIED before the migration).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
IF OBJECT_ID(N'dbo.LegalWatchRuns', N'U') IS NULL AND OBJECT_ID(N'dbo.LegalWatchItems', N'U') IS NULL AND COL_LENGTH(N'dbo.LegalDocuments', N'SourceUrl') IS NULL
BEGIN PRINT N'INFO: dbo.LegalWatchRuns, dbo.LegalWatchItems and LegalDocuments.SourceUrl missing'; PRINT N'VERIFY_STATUS=NOT_APPLIED'; RETURN; END
DECLARE @fail int = 0;
IF OBJECT_ID(N'dbo.LegalWatchRuns', N'U') IS NULL BEGIN PRINT N'FAIL: dbo.LegalWatchRuns missing'; SET @fail += 1; END
IF OBJECT_ID(N'dbo.LegalWatchItems', N'U') IS NULL BEGIN PRINT N'FAIL: dbo.LegalWatchItems missing'; SET @fail += 1; END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_LegalWatchRuns_Period' AND object_id = OBJECT_ID(N'dbo.LegalWatchRuns') AND is_unique = 1)
    BEGIN PRINT N'FAIL: UX_LegalWatchRuns_Period missing'; SET @fail += 1; END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_LegalWatchItems_New' AND object_id = OBJECT_ID(N'dbo.LegalWatchItems') AND is_unique = 1 AND has_filter = 1)
    BEGIN PRINT N'FAIL: UX_LegalWatchItems_New missing'; SET @fail += 1; END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_LegalWatchItems_Status' AND object_id = OBJECT_ID(N'dbo.LegalWatchItems') AND is_unique = 1 AND has_filter = 1)
    BEGIN PRINT N'FAIL: UX_LegalWatchItems_Status missing'; SET @fail += 1; END
IF OBJECT_ID(N'dbo.CK_LegalWatchItems_Status', N'C') IS NULL OR OBJECT_ID(N'dbo.CK_LegalWatchRuns_Kind', N'C') IS NULL
    BEGIN PRINT N'FAIL: LegalWatch check constraints missing'; SET @fail += 1; END
IF COL_LENGTH(N'dbo.LegalDocuments', N'SourceUrl') IS NULL OR COL_LENGTH(N'dbo.LegalDocuments', N'EffectStatus') IS NULL OR COL_LENGTH(N'dbo.LegalDocuments', N'StatusCheckedAt') IS NULL
    BEGIN PRINT N'FAIL: LegalDocuments.SourceUrl/EffectStatus/StatusCheckedAt missing'; SET @fail += 1; END
IF @fail = 0 PRINT N'PASS: legal watch runs, queue and library status columns';
PRINT N'VERIFY_STATUS=' + CASE WHEN @fail = 0 THEN N'PASS' ELSE N'FAIL' END;
