-- READ ONLY; SQL Server 2014; output only Messages.
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
    THROW 51000, 'FAIL: Expected database TAPortalSys', 1;
DECLARE @fail int=0, @warn int=0, @n int, @active int, @bad int, @dup int;
DECLARE @msg nvarchar(2000);
PRINT N'=== CUSTOMER PORTAL POST-MIGRATION VERIFY ===';
PRINT N'PASS: Database TAPortalSys';
IF OBJECT_ID(N'dbo.CustomerPortalMemberships',N'U') IS NULL
    THROW 51001, 'FAIL: Membership table missing; VERIFY_STATUS=FAIL', 1;
PRINT N'PASS: dbo.CustomerPortalMemberships exists';
IF EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.CustomerPortalMemberships') AND name=N'FK_CPM_User' AND is_disabled=0 AND is_not_trusted=0)
 PRINT N'PASS: FK_CPM_User';
ELSE BEGIN PRINT N'FAIL: FK_CPM_User missing/disabled/untrusted'; SET @fail+=1; END;
IF EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.CustomerPortalMemberships') AND name=N'FK_CPM_Customer' AND is_disabled=0 AND is_not_trusted=0)
 PRINT N'PASS: FK_CPM_Customer';
ELSE BEGIN PRINT N'FAIL: FK_CPM_Customer missing/disabled/untrusted'; SET @fail+=1; END;
IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.CustomerPortalMemberships') AND name=N'CK_CPM_Role' AND is_disabled=0 AND is_not_trusted=0)
 PRINT N'PASS: CK_CPM_Role';
ELSE BEGIN PRINT N'FAIL: CK_CPM_Role missing/disabled/untrusted'; SET @fail+=1; END;
IF EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.CustomerPortalMemberships') AND name=N'UQ_CPM_UserCustomer' AND is_unique=1 AND is_disabled=0)
 PRINT N'PASS: UQ_CPM_UserCustomer';
ELSE BEGIN PRINT N'FAIL: UQ_CPM_UserCustomer missing/disabled'; SET @fail+=1; END;
IF EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.CustomerPortalMemberships') AND name=N'IX_CPM_Customer' AND is_disabled=0)
 PRINT N'PASS: IX_CPM_Customer';
ELSE BEGIN PRINT N'FAIL: IX_CPM_Customer missing/disabled'; SET @fail+=1; END;
IF OBJECT_ID(N'dbo.Permissions',N'U') IS NULL
 THROW 51002, 'FAIL: dbo.Permissions missing; VERIFY_STATUS=FAIL',1;
SELECT @n=COUNT(*),@active=COALESCE(SUM(CASE WHEN IsActive=1 AND IsDeleted=0 THEN 1 ELSE 0 END),0)
FROM dbo.Permissions WHERE Code='CUSTOMERS.PORTAL.MANAGE';
SET @msg=N'CUSTOMERS.PORTAL.MANAGE: total='+CONVERT(nvarchar(20),@n)+N', active='+CONVERT(nvarchar(20),@active);
IF @n=1 AND @active=1 PRINT N'PASS: '+@msg;
ELSE BEGIN PRINT N'FAIL: '+@msg; SET @fail+=1; END;
SELECT @n=COUNT(*),@active=COALESCE(SUM(CASE WHEN IsActive=1 AND IsDeleted=0 THEN 1 ELSE 0 END),0)
FROM dbo.Permissions WHERE Code='CUSTOMERS.VIEW';
SET @msg=N'CUSTOMERS.VIEW: total='+CONVERT(nvarchar(20),@n)+N', active='+CONVERT(nvarchar(20),@active);
IF @n=1 AND @active=1 PRINT N'PASS: '+@msg;
ELSE BEGIN PRINT N'FAIL: '+@msg; SET @fail+=1; END;
SELECT @bad=COUNT(*) FROM dbo.CustomerPortalMemberships WHERE PortalRole IS NULL OR PortalRole NOT IN ('OWNER','ADMIN','VIEWER');
IF @bad=0 PRINT N'PASS: No invalid PortalRole';
ELSE BEGIN PRINT N'FAIL: Invalid PortalRole rows='+CONVERT(nvarchar(20),@bad); SET @fail+=1; END;
SELECT @dup=COUNT(*) FROM (SELECT UserId,CustomerId FROM dbo.CustomerPortalMemberships GROUP BY UserId,CustomerId HAVING COUNT(*)>1) d;
IF @dup=0 PRINT N'PASS: No duplicate memberships';
ELSE BEGIN PRINT N'FAIL: Duplicate membership pairs='+CONVERT(nvarchar(20),@dup); SET @fail+=1; END;
SELECT @n=COUNT(*),@active=COALESCE(SUM(CASE WHEN IsActive=1 THEN 1 ELSE 0 END),0) FROM dbo.CustomerPortalMemberships;
PRINT N'INFO: Memberships='+CONVERT(nvarchar(20),@n)+N', active='+CONVERT(nvarchar(20),@active);
IF @n=0 BEGIN PRINT N'WARN: No memberships; account provisioning still required'; SET @warn+=1; END;
SELECT @n=COUNT(*) FROM dbo.Users;
PRINT N'INFO: Users='+CONVERT(nvarchar(20),@n);
SELECT @n=COUNT(*) FROM dbo.Customers;
PRINT N'INFO: Customers='+CONVERT(nvarchar(20),@n);
PRINT N'INFO: Failed checks='+CONVERT(nvarchar(20),@fail)+N', warnings='+CONVERT(nvarchar(20),@warn);
IF @fail>0 THROW 51003,'VERIFY_STATUS=FAIL. Review FAIL messages; no changes executed.',1;
IF @warn>0 PRINT N'VERIFY_STATUS=PASS_WITH_WARNINGS';
ELSE PRINT N'VERIFY_STATUS=PASS';
PRINT N'NEXT: Verify real membership/login/tenant isolation in application.';
