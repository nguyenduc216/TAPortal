-- CONTRACT-REVIEW - rollback of migrations/044-contract-review.sql.
-- Drops the contract review tables and the law library, the CONTRACT_REVIEW_CODE sequence, and the permissions inserted by
-- 044 (marker rows) with their grants. Refuses (THROW 51002) while any revision request or law document exists.
-- After it, database/scripts/077-verify-contract-review.sql must print VERIFY_STATUS=NOT_APPLIED.
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @Marker uniqueidentifier = 'A0000044-0000-4000-8000-000000000044';
-- Refuses while real work exists: revision requests or law documents must be removed by the operator first.
DECLARE @reviews int = 0, @laws int = 0;
IF OBJECT_ID(N'dbo.ContractReviews', N'U') IS NOT NULL EXEC sp_executesql N'SELECT @n = COUNT(1) FROM dbo.ContractReviews;', N'@n int OUTPUT', @n = @reviews OUTPUT;
IF OBJECT_ID(N'dbo.LegalDocuments', N'U') IS NOT NULL EXEC sp_executesql N'SELECT @n = COUNT(1) FROM dbo.LegalDocuments;', N'@n int OUTPUT', @n = @laws OUTPUT;
IF @reviews > 0 OR @laws > 0
 THROW 51002, 'Rollback refused: dbo.ContractReviews or dbo.LegalDocuments contains rows; no changes executed', 1;
IF OBJECT_ID(N'dbo.ContractReviewChanges', N'U') IS NOT NULL BEGIN DROP TABLE dbo.ContractReviewChanges; PRINT N'dbo.ContractReviewChanges: dropped'; END
IF OBJECT_ID(N'dbo.ContractReviewFindings', N'U') IS NOT NULL BEGIN DROP TABLE dbo.ContractReviewFindings; PRINT N'dbo.ContractReviewFindings: dropped'; END
IF OBJECT_ID(N'dbo.ContractReviewRuns', N'U') IS NOT NULL BEGIN DROP TABLE dbo.ContractReviewRuns; PRINT N'dbo.ContractReviewRuns: dropped'; END
IF OBJECT_ID(N'dbo.ContractReviewParagraphs', N'U') IS NOT NULL BEGIN DROP TABLE dbo.ContractReviewParagraphs; PRINT N'dbo.ContractReviewParagraphs: dropped'; END
IF OBJECT_ID(N'dbo.ContractReviewVersions', N'U') IS NOT NULL BEGIN DROP TABLE dbo.ContractReviewVersions; PRINT N'dbo.ContractReviewVersions: dropped'; END
IF OBJECT_ID(N'dbo.ContractReviewFiles', N'U') IS NOT NULL BEGIN DROP TABLE dbo.ContractReviewFiles; PRINT N'dbo.ContractReviewFiles: dropped'; END
IF OBJECT_ID(N'dbo.ContractReviews', N'U') IS NOT NULL BEGIN DROP TABLE dbo.ContractReviews; PRINT N'dbo.ContractReviews: dropped'; END
IF OBJECT_ID(N'dbo.LegalArticles', N'U') IS NOT NULL BEGIN DROP TABLE dbo.LegalArticles; PRINT N'dbo.LegalArticles: dropped'; END
IF OBJECT_ID(N'dbo.LegalDocuments', N'U') IS NOT NULL BEGIN DROP TABLE dbo.LegalDocuments; PRINT N'dbo.LegalDocuments: dropped'; END
DELETE FROM dbo.NumberSequences WHERE Code = 'CONTRACT_REVIEW_CODE';
PRINT N'INFO: sequences removed = ' + CAST(@@ROWCOUNT AS nvarchar(10));
DECLARE @perm TABLE (Id uniqueidentifier PRIMARY KEY);
INSERT @perm SELECT Id FROM dbo.Permissions
WHERE Code IN ('CONTRACTS.REVIEW.VIEW','CONTRACTS.REVIEW.CREATE','CONTRACTS.REVIEW.RUN_AI','CONTRACTS.REVIEW.EDIT','CONTRACTS.REVIEW.FINALIZE','LEGAL.LIBRARY.MANAGE')
  AND CreatedBy = @Marker;
DELETE rp FROM dbo.RolePermissions rp JOIN @perm p ON p.Id = rp.PermissionId;
PRINT N'INFO: role grants removed = ' + CAST(@@ROWCOUNT AS nvarchar(10));
IF OBJECT_ID(N'dbo.UserPermissions', N'U') IS NOT NULL
BEGIN
    DELETE up FROM dbo.UserPermissions up JOIN @perm p ON p.Id = up.PermissionId;
    PRINT N'INFO: user grants removed = ' + CAST(@@ROWCOUNT AS nvarchar(10));
END
DELETE pm FROM dbo.Permissions pm JOIN @perm p ON p.Id = pm.Id;
PRINT N'INFO: permissions removed = ' + CAST(@@ROWCOUNT AS nvarchar(10));
COMMIT TRANSACTION;
PRINT N'078-rollback-contract-review: done';
