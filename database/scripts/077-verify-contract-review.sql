-- CONTRACT-REVIEW - READ ONLY; SQL Server 2014; output only Messages.
-- Run after migrations/044-contract-review.sql. Expected last line: VERIFY_STATUS=PASS (NOT_APPLIED before the migration).
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
IF OBJECT_ID(N'dbo.ContractReviews', N'U') IS NULL BEGIN PRINT N'INFO: dbo.ContractReviews missing'; PRINT N'VERIFY_STATUS=NOT_APPLIED'; RETURN; END
DECLARE @fail int = 0;
IF OBJECT_ID(N'dbo.ContractReviewFiles', N'U') IS NULL BEGIN PRINT N'FAIL: dbo.ContractReviewFiles missing'; SET @fail += 1; END
IF OBJECT_ID(N'dbo.ContractReviewVersions', N'U') IS NULL BEGIN PRINT N'FAIL: dbo.ContractReviewVersions missing'; SET @fail += 1; END
IF OBJECT_ID(N'dbo.ContractReviewParagraphs', N'U') IS NULL BEGIN PRINT N'FAIL: dbo.ContractReviewParagraphs missing'; SET @fail += 1; END
IF OBJECT_ID(N'dbo.ContractReviewRuns', N'U') IS NULL BEGIN PRINT N'FAIL: dbo.ContractReviewRuns missing'; SET @fail += 1; END
IF OBJECT_ID(N'dbo.ContractReviewFindings', N'U') IS NULL BEGIN PRINT N'FAIL: dbo.ContractReviewFindings missing'; SET @fail += 1; END
IF OBJECT_ID(N'dbo.ContractReviewChanges', N'U') IS NULL BEGIN PRINT N'FAIL: dbo.ContractReviewChanges missing'; SET @fail += 1; END
IF OBJECT_ID(N'dbo.LegalDocuments', N'U') IS NULL BEGIN PRINT N'FAIL: dbo.LegalDocuments missing'; SET @fail += 1; END
IF OBJECT_ID(N'dbo.LegalArticles', N'U') IS NULL BEGIN PRINT N'FAIL: dbo.LegalArticles missing'; SET @fail += 1; END
IF OBJECT_ID(N'dbo.CK_ContractReviews_Status', N'C') IS NULL BEGIN PRINT N'FAIL: CK_ContractReviews_Status missing'; SET @fail += 1; END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ContractReviewParagraphs_Key' AND object_id = OBJECT_ID(N'dbo.ContractReviewParagraphs'))
    BEGIN PRINT N'FAIL: UX_ContractReviewParagraphs_Key missing'; SET @fail += 1; END
IF NOT EXISTS (SELECT 1 FROM dbo.NumberSequences WHERE Code = 'CONTRACT_REVIEW_CODE' AND IsActive = 1)
    BEGIN PRINT N'FAIL: NumberSequences CONTRACT_REVIEW_CODE missing'; SET @fail += 1; END
DECLARE @codes TABLE (Code varchar(200) COLLATE DATABASE_DEFAULT PRIMARY KEY);
INSERT @codes (Code) VALUES ('CONTRACTS.REVIEW.VIEW'), ('CONTRACTS.REVIEW.CREATE'), ('CONTRACTS.REVIEW.RUN_AI'), ('CONTRACTS.REVIEW.EDIT'), ('CONTRACTS.REVIEW.FINALIZE'), ('LEGAL.LIBRARY.MANAGE');
DECLARE @missing int = (SELECT COUNT(1) FROM @codes c WHERE NOT EXISTS (
    SELECT 1 FROM dbo.Permissions p JOIN dbo.RolePermissions rp ON rp.PermissionId = p.Id JOIN dbo.Roles r ON r.Id = rp.RoleId
    WHERE p.Code = c.Code AND p.IsDeleted = 0 AND r.Code = 'SYS_ADMIN'));
IF @missing > 0 BEGIN PRINT N'FAIL: permissions missing or not granted to SYS_ADMIN = ' + CAST(@missing AS nvarchar(10)); SET @fail += 1; END
IF @fail = 0 PRINT N'PASS: contract review tables, law library, sequence and permissions (marker A0000044-0000-4000-8000-000000000044)';
PRINT N'VERIFY_STATUS=' + CASE WHEN @fail = 0 THEN N'PASS' ELSE N'FAIL' END;
