-- Read-only post-migration verification.
SELECT DB_NAME() AS CurrentDatabase;
IF OBJECT_ID('dbo.CustomerPortalMemberships','U') IS NULL THROW 51000,'Membership table missing',1;
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name='FK_CPM_User') THROW 51000,'User FK missing',1;
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name='FK_CPM_Customer') THROW 51000,'Customer FK missing',1;
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name='CK_CPM_Role' AND is_disabled=0) THROW 51000,'Role check missing',1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.CustomerPortalMemberships') AND name='UQ_CPM_UserCustomer' AND is_unique=1) THROW 51000,'Unique pair missing',1;
SELECT * FROM dbo.CustomerPortalMemberships WHERE PortalRole NOT IN ('OWNER','ADMIN','VIEWER');
SELECT UserId,CustomerId,COUNT(*) FROM dbo.CustomerPortalMemberships GROUP BY UserId,CustomerId HAVING COUNT(*)>1;
SELECT COUNT(*) AS UsersCount FROM dbo.Users;
SELECT COUNT(*) AS CustomersCount FROM dbo.Customers;
