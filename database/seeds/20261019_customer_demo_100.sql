/* Manual, non-destructive demo seed. Adds KH0011..KH0100 only when absent.
   Requires the customer-profile migration. Does not delete existing customers. */
SET XACT_ABORT ON;
IF DB_NAME() <> N'TAPortalSys' THROW 51101, 'Wrong database; expected TAPortalSys.', 1;
IF OBJECT_ID(N'dbo.CustomerContacts', N'U') IS NULL OR COL_LENGTH(N'dbo.Customers', N'LogoUrl') IS NULL
    THROW 51102, 'Run migration 20261019_customer-profile.sql first.', 1;
BEGIN TRY
BEGIN TRAN;
;WITH Numbers AS (
    SELECT 11 AS n UNION ALL SELECT n+1 FROM Numbers WHERE n<100
)
INSERT dbo.Customers(Code,Name,TaxCode,Email,Phone,Address,Status,IsActive)
SELECT 'KH'+RIGHT('0000'+CAST(n AS varchar(4)),4),
    N'Công ty ' + CASE n%3 WHEN 0 THEN N'Cổ phần ' ELSE N'TNHH ' END
        + CASE n%5 WHEN 0 THEN N'Bình Minh' WHEN 1 THEN N'Hải Âu' WHEN 2 THEN N'An Phát' WHEN 3 THEN N'Mộc Lan' ELSE N'Đại Dương' END
        + N' ' + RIGHT('000'+CAST(n AS varchar(3)),3),
    '0318'+RIGHT('000000'+CAST(n AS varchar(6)),6),
    'lienhe'+CAST(n AS varchar(3))+'@demo.example',
    '0283810'+RIGHT('0000'+CAST(n AS varchar(4)),4),
    CAST(10+n AS nvarchar(10))+N' Nguyễn Văn Linh, Quận 7, TP. Hồ Chí Minh',
    'ACTIVE',1
FROM Numbers
WHERE NOT EXISTS (SELECT 1 FROM dbo.Customers WHERE Code='KH'+RIGHT('0000'+CAST(n AS varchar(4)),4));
INSERT dbo.CustomerContacts(CustomerId,Name,Role,Email,Phone,SortOrder)
SELECT c.Id,N'Trần Minh '+RIGHT(N'000'+CAST(SUBSTRING(c.Code,3,4) AS nvarchar(4)),3),N'Đầu mối liên hệ',c.Email,c.Phone,0
FROM dbo.Customers c
WHERE c.Code BETWEEN 'KH0011' AND 'KH0100' AND c.IsDeleted=0
AND NOT EXISTS (SELECT 1 FROM dbo.CustomerContacts cc WHERE cc.CustomerId=c.Id AND cc.IsDeleted=0);
COMMIT;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK;
    THROW;
END CATCH;
SELECT COUNT(*) AS DemoCustomers FROM dbo.Customers WHERE Code BETWEEN 'KH0001' AND 'KH0100' AND IsDeleted=0;