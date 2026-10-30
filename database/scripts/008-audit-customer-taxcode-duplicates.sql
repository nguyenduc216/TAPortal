/* TAPORTAL-SYSTEM-AUDIT-CHG-20261028T173600Z-62724B — AUD-16.
   READ-ONLY audit of dbo.Customers.TaxCode before optional migration 009 (unique index).
   SQL Server 2014+. No DDL/DML, no USE. Output: PRINT messages ending with VERIFY_STATUS=PASS|WARN|FAIL.
   Normalization mirrors the application: whitespace removed; branch dash and leading zeros kept. */
SET NOCOUNT ON;
DECLARE @fail int = 0, @warn int = 0, @n int, @line nvarchar(400);

IF DB_NAME() <> N'TAPortalSys'
BEGIN PRINT N'FAIL: current database is ' + DB_NAME() + N'; select TAPortalSys.'; PRINT N'VERIFY_STATUS=FAIL'; RETURN; END
IF OBJECT_ID(N'dbo.Customers', N'U') IS NULL OR COL_LENGTH(N'dbo.Customers', N'TaxCode') IS NULL OR COL_LENGTH(N'dbo.Customers', N'IsDeleted') IS NULL
BEGIN PRINT N'FAIL: dbo.Customers/TaxCode/IsDeleted missing'; PRINT N'VERIFY_STATUS=FAIL'; RETURN; END

DECLARE @t TABLE (Id uniqueidentifier, Code varchar(50), Raw nvarchar(100), Norm nvarchar(100));
INSERT @t SELECT Id, Code, TaxCode, REPLACE(REPLACE(REPLACE(TaxCode, ' ', ''), CHAR(9), ''), CHAR(160), '')
FROM dbo.Customers WHERE IsDeleted = 0 AND TaxCode IS NOT NULL AND LTRIM(RTRIM(TaxCode)) <> '';

SELECT @n = COUNT(1) FROM @t; PRINT N'INFO: non-deleted customers with tax code: ' + CONVERT(nvarchar(10), @n);

SELECT @n = COUNT(1) FROM (SELECT Norm FROM @t GROUP BY Norm HAVING COUNT(1) > 1) d;
IF @n > 0
BEGIN
    SET @fail += 1; PRINT N'FAIL: ' + CONVERT(nvarchar(10), @n) + N' normalized tax code(s) shared by several non-deleted customers:';
    DECLARE d CURSOR LOCAL FAST_FORWARD FOR
        SELECT N'  ' + t.Norm + N' -> ' + t.Code FROM @t t WHERE t.Norm IN (SELECT Norm FROM @t GROUP BY Norm HAVING COUNT(1) > 1) ORDER BY t.Norm, t.Code;
    OPEN d; FETCH NEXT FROM d INTO @line;
    WHILE @@FETCH_STATUS = 0 BEGIN PRINT @line; FETCH NEXT FROM d INTO @line; END
    CLOSE d; DEALLOCATE d;
END
ELSE PRINT N'PASS: no duplicate normalized tax codes among non-deleted customers';

SELECT @n = COUNT(1) FROM @t WHERE Raw <> Norm;
IF @n > 0 BEGIN SET @warn += 1; PRINT N'WARN: ' + CONVERT(nvarchar(10), @n) + N' tax code(s) contain whitespace; a raw-value unique index would not see them as equal. Review before migration 009.'; END
ELSE PRINT N'PASS: no whitespace in stored tax codes';

SELECT @n = COUNT(1) FROM @t WHERE NOT (Norm LIKE REPLICATE('[0-9]', 10) OR Norm LIKE REPLICATE('[0-9]', 10) + '-[0-9][0-9][0-9]');
IF @n > 0 BEGIN SET @warn += 1; PRINT N'WARN: ' + CONVERT(nvarchar(10), @n) + N' tax code(s) not in 10-digit or 10-3 format (kept as-is; the app only validates new or changed values).'; END
ELSE PRINT N'PASS: all tax codes use 10-digit or 10-3 format';

IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Customers') AND name = N'UX_Customers_TaxCode')
    PRINT N'INFO: UX_Customers_TaxCode already exists (database-level uniqueness active)';
ELSE PRINT N'INFO: UX_Customers_TaxCode not present; uniqueness enforced by the Admin application only';

PRINT N'SUMMARY: failures=' + CONVERT(nvarchar(10), @fail) + N', warnings=' + CONVERT(nvarchar(10), @warn);
IF @fail > 0 PRINT N'NEXT: resolve duplicates by business decision (do not run migration 009 until PASS).';
ELSE PRINT N'NEXT: optional — review and run database/migrations/009-customer-taxcode-unique-index.sql.';
PRINT N'VERIFY_STATUS=' + CASE WHEN @fail > 0 THEN N'FAIL' WHEN @warn > 0 THEN N'WARN' ELSE N'PASS' END;
