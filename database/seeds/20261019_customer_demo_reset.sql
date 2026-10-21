/* DESTRUCTIVE DEMO RESET: manual execution only. Back up first.
   Removes CRM assignments/shares/teams and contacts; refuses to erase financial or unknown dependencies. */
SET XACT_ABORT ON;
IF DB_NAME() <> N'TAPortalSys' THROW 51001, 'Wrong database; expected TAPortalSys.', 1;
IF OBJECT_ID(N'dbo.CustomerContacts',N'U') IS NULL OR COL_LENGTH(N'dbo.Customers',N'LogoUrl') IS NULL
    THROW 51002, 'Run migration 20261019_customer-profile.sql first.', 1;
BEGIN TRY
BEGIN TRAN;
/* Detect every FK referencing Customers except the four explicitly reset below.
   This also catches finance tables introduced by future migrations. */
DECLARE @sql nvarchar(max) = N'', @blocked bit = 0;
SELECT @sql = @sql + N'IF EXISTS (SELECT 1 FROM ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name)
    + N' AS child JOIN dbo.Customers AS customer ON '
    + STUFF((SELECT N' AND child.' + QUOTENAME(pc.name) + N'=customer.' + QUOTENAME(rc.name)
        FROM sys.foreign_key_columns fkc
        JOIN sys.columns pc ON pc.object_id=fkc.parent_object_id AND pc.column_id=fkc.parent_column_id
        JOIN sys.columns rc ON rc.object_id=fkc.referenced_object_id AND rc.column_id=fkc.referenced_column_id
        WHERE fkc.constraint_object_id=fk.object_id ORDER BY fkc.constraint_column_id FOR XML PATH(''),TYPE).value('.','nvarchar(max)'),1,5,N'')
    + N') SET @blocked = 1;' + CHAR(10)
FROM sys.foreign_keys fk JOIN sys.tables t ON t.object_id=fk.parent_object_id
WHERE fk.referenced_object_id=OBJECT_ID(N'dbo.Customers')
  AND NOT (SCHEMA_NAME(t.schema_id)=N'dbo' AND t.name IN (N'CustomerContacts',N'CustomerEmployeeAssignments',N'CustomerBranchShares',N'CustomerServiceTeams'));
IF @sql <> N'' EXEC sys.sp_executesql @sql, N'@blocked bit OUTPUT', @blocked OUTPUT;
IF @blocked = 1 THROW 51003, 'Other customer-linked records exist (e.g. finance/invoices). Back up and resolve these references before reset.', 1;
DELETE FROM dbo.CustomerEmployeeAssignments;
DELETE FROM dbo.CustomerBranchShares;
DELETE FROM dbo.CustomerServiceTeams;
DELETE FROM dbo.CustomerContacts;
DELETE FROM dbo.Customers;
INSERT dbo.Customers(Code,Name,TaxCode,Email,Phone,Address,LogoUrl,Status,IsActive)
VALUES
('KH0001',N'Công ty TNHH Sao Biển','0312345671','hello@saobien.example','02838201001',N'12 Nguyễn Huệ, Quận 1, TP. Hồ Chí Minh',NULL,'ACTIVE',1),
('KH0002',N'Công ty Cổ phần Mây Trắng','0312345672','contact@maytrang.example','02838201002',N'24 Đồng Khởi, Quận 1, TP. Hồ Chí Minh',NULL,'ACTIVE',1),
('KH0003',N'Công ty TNHH Vườn Xanh','0312345673','info@vuonxanh.example','02838201003',N'36 Lê Lợi, Quận 1, TP. Hồ Chí Minh',NULL,'ACTIVE',1),
('KH0004',N'Công ty Cổ phần Ánh Dương','0312345674','hello@anhduong.example','02838201004',N'48 Pasteur, Quận 3, TP. Hồ Chí Minh',NULL,'ACTIVE',1),
('KH0005',N'Công ty TNHH Hải Đăng','0312345675','contact@haidang.example','02838201005',N'60 Võ Văn Tần, Quận 3, TP. Hồ Chí Minh',NULL,'ACTIVE',1),
('KH0006',N'Công ty Cổ phần Hương Việt','0102345676','info@huongviet.example','02438201006',N'72 Tràng Tiền, Hoàn Kiếm, Hà Nội',NULL,'ACTIVE',1),
('KH0007',N'Công ty TNHH Tre Việt','0102345677','hello@treviet.example','02438201007',N'84 Hai Bà Trưng, Hoàn Kiếm, Hà Nội',NULL,'ACTIVE',1),
('KH0008',N'Công ty Cổ phần Bình Minh','0102345678','contact@binhminh.example','02438201008',N'96 Lý Thường Kiệt, Hoàn Kiếm, Hà Nội',NULL,'ACTIVE',1),
('KH0009',N'Công ty TNHH Sen Hồng','0402345679','info@senhong.example','02363820109',N'108 Bạch Đằng, Hải Châu, Đà Nẵng',NULL,'ACTIVE',1),
('KH0010',N'Công ty Cổ phần Sông Lam','0402345680','hello@songlam.example','02363820110',N'120 Trần Phú, Hải Châu, Đà Nẵng',NULL,'ACTIVE',1);
INSERT dbo.CustomerContacts(CustomerId,Name,Role,Email,Phone,SortOrder)
SELECT Id, N'Nguyễn Minh Anh', N'Đầu mối hợp tác', Email, Phone, 0 FROM dbo.Customers WHERE Code LIKE 'KH00__';
COMMIT;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK;
    THROW;
END CATCH;
SELECT Code,Name FROM dbo.Customers ORDER BY Code;