/* TAPORTAL-SYSTEM-AUDIT-CHG-20261028T173600Z-62724B — AUD-25 preflight.
   READ-ONLY verification for dbo.AuditLogs used by Admin role changes, user updates and user soft delete.
   SQL Server 2014+. No DDL/DML, no USE statement. Output: PRINT messages ending with VERIFY_STATUS=PASS|WARN|FAIL.
   Optional: set @AppPrincipal to the DATABASE USER name of the TAPortal Admin application to check its
   effective permissions (uses EXECUTE AS USER ... REVERT; requires IMPERSONATE, otherwise NOT_VERIFIED). */
SET NOCOUNT ON;
DECLARE @AppPrincipal sysname = N'';   -- e.g. N'taportal_admin_app'; leave empty if unknown
DECLARE @fail int = 0, @warn int = 0, @msg nvarchar(2000);
DECLARE @obj int = OBJECT_ID(N'dbo.AuditLogs', N'U');

IF DB_NAME() <> N'TAPortalSys'
BEGIN
    PRINT N'FAIL: current database is ' + DB_NAME() + N'; select TAPortalSys and rerun.';
    PRINT N'VERIFY_STATUS=FAIL';
    RETURN;
END
PRINT N'PASS: database TAPortalSys';

IF @obj IS NULL
BEGIN
    PRINT N'FAIL: dbo.AuditLogs does not exist (or is not visible to the current login).';
    PRINT N'NEXT: review and run database/migrations/010-create-audit-logs-if-missing.sql, then rerun this script.';
    PRINT N'VERIFY_STATUS=FAIL';
    RETURN;
END
PRINT N'PASS: dbo.AuditLogs exists';

/* Columns written by the application: UserId, Action, EntityName, EntityId, OldValues, NewValues, IpAddress. */
DECLARE @cols TABLE (Name sysname, TypeName sysname, MinChars int, NeedMax bit);
INSERT @cols VALUES (N'UserId', N'uniqueidentifier', 0, 0), (N'Action', N'nvarchar', 30, 0), (N'EntityName', N'nvarchar', 20, 0),
                    (N'EntityId', N'nvarchar', 36, 0), (N'OldValues', N'nvarchar', 0, 1), (N'NewValues', N'nvarchar', 0, 1), (N'IpAddress', N'varchar', 45, 0);
DECLARE @name sysname, @type sysname, @min int, @needMax bit, @actualType sysname, @len int;
DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT Name, TypeName, MinChars, NeedMax FROM @cols;
OPEN c; FETCH NEXT FROM c INTO @name, @type, @min, @needMax;
WHILE @@FETCH_STATUS = 0
BEGIN
    SELECT @actualType = TYPE_NAME(sc.user_type_id),
           @len = CASE WHEN sc.max_length = -1 THEN -1 WHEN TYPE_NAME(sc.user_type_id) IN (N'nvarchar', N'nchar') THEN sc.max_length / 2 ELSE sc.max_length END
    FROM sys.columns sc WHERE sc.object_id = @obj AND sc.name = @name;
    IF @@ROWCOUNT = 0
    BEGIN SET @fail += 1; PRINT N'FAIL: column ' + @name + N' missing'; END
    ELSE IF @type = N'uniqueidentifier' AND @actualType <> N'uniqueidentifier'
    BEGIN SET @fail += 1; PRINT N'FAIL: column ' + @name + N' type ' + @actualType + N' (expected uniqueidentifier)'; END
    ELSE IF @type <> N'uniqueidentifier' AND @actualType NOT IN (N'nvarchar', N'varchar', N'nchar', N'char')
    BEGIN SET @fail += 1; PRINT N'FAIL: column ' + @name + N' type ' + @actualType + N' is not a string type'; END
    ELSE IF @needMax = 1 AND @len <> -1
    BEGIN SET @warn += 1; PRINT N'WARN: column ' + @name + N' length ' + CONVERT(nvarchar(20), @len) + N' (expected max); long values would fail the insert'; END
    ELSE IF @min > 0 AND @len <> -1 AND @len < @min
    BEGIN SET @fail += 1; PRINT N'FAIL: column ' + @name + N' length ' + CONVERT(nvarchar(20), @len) + N' < ' + CONVERT(nvarchar(20), @min); END
    ELSE PRINT N'PASS: column ' + @name + N' ' + @actualType + N'(' + CASE WHEN @len = -1 THEN N'max' ELSE CONVERT(nvarchar(20), @len) END + N')';
    FETCH NEXT FROM c INTO @name, @type, @min, @needMax;
END
CLOSE c; DEALLOCATE c;

/* Any other NOT NULL column must have a default, identity or computed value, otherwise every insert fails. */
DECLARE @blocking nvarchar(1000) = N'';
SELECT @blocking = @blocking + sc.name + N' '
FROM sys.columns sc
WHERE sc.object_id = @obj AND sc.is_nullable = 0 AND sc.is_identity = 0 AND sc.is_computed = 0 AND sc.default_object_id = 0
  AND TYPE_NAME(sc.user_type_id) NOT IN (N'timestamp', N'rowversion')
  AND sc.name NOT IN (N'UserId', N'Action', N'EntityName', N'EntityId', N'OldValues', N'NewValues', N'IpAddress');
IF @blocking <> N'' BEGIN SET @fail += 1; PRINT N'FAIL: NOT NULL columns without default: ' + @blocking; END
ELSE PRINT N'PASS: no other NOT NULL column blocks inserts';

IF COL_LENGTH(N'dbo.AuditLogs', N'CreatedAt') IS NULL
BEGIN SET @warn += 1; PRINT N'WARN: CreatedAt column missing; audit rows would have no timestamp'; END
ELSE IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @obj AND name = N'CreatedAt' AND default_object_id = 0)
BEGIN SET @warn += 1; PRINT N'WARN: CreatedAt has no default; application does not supply it'; END
ELSE PRINT N'PASS: CreatedAt has a default (timestamp recorded by the database)';

IF EXISTS (SELECT 1 FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
           WHERE fk.parent_object_id = @obj AND fk.referenced_object_id = OBJECT_ID(N'dbo.Users') AND COL_NAME(fkc.parent_object_id, fkc.parent_column_id) = N'UserId' AND fk.is_disabled = 0)
    PRINT N'PASS: FK AuditLogs.UserId -> Users enabled';
ELSE BEGIN SET @warn += 1; PRINT N'WARN: no enabled FK AuditLogs.UserId -> Users (actor integrity not enforced by the database)'; END

IF EXISTS (SELECT 1 FROM sys.triggers WHERE parent_id = @obj AND is_disabled = 0)
BEGIN SET @warn += 1; PRINT N'WARN: enabled triggers exist on dbo.AuditLogs; review that they cannot block inserts'; END

SET @msg = N'INFO: existing USER_ROLES_UPDATED/USER_UPDATED/USER_SOFT_DELETED rows: ' +
    CONVERT(nvarchar(20), (SELECT COUNT(1) FROM dbo.AuditLogs WHERE Action IN (N'USER_ROLES_UPDATED', N'USER_UPDATED', N'USER_SOFT_DELETED')));
PRINT @msg;

/* Effective permissions of the Admin application principal (no data is changed). */
IF @AppPrincipal = N''
BEGIN SET @warn += 1; PRINT N'NOT_VERIFIED: @AppPrincipal not set; Admin application INSERT permission on dbo.AuditLogs unknown'; END
ELSE IF DATABASE_PRINCIPAL_ID(@AppPrincipal) IS NULL
BEGIN SET @fail += 1; PRINT N'FAIL: database user ' + @AppPrincipal + N' not found in TAPortalSys'; END
ELSE
BEGIN
    BEGIN TRY
        DECLARE @insAudit int, @updUsers int, @insRoles int, @delRoles int, @insCust int, @updCust int;
        EXECUTE AS USER = @AppPrincipal;
        SELECT @insAudit = HAS_PERMS_BY_NAME(N'dbo.AuditLogs', N'OBJECT', N'INSERT'),
               @updUsers = HAS_PERMS_BY_NAME(N'dbo.Users', N'OBJECT', N'UPDATE'),
               @insRoles = HAS_PERMS_BY_NAME(N'dbo.UserRoles', N'OBJECT', N'INSERT'),
               @delRoles = HAS_PERMS_BY_NAME(N'dbo.UserRoles', N'OBJECT', N'DELETE'),
               @insCust = HAS_PERMS_BY_NAME(N'dbo.Customers', N'OBJECT', N'INSERT'),
               @updCust = HAS_PERMS_BY_NAME(N'dbo.Customers', N'OBJECT', N'UPDATE');
        REVERT;
        IF ISNULL(@insAudit, 0) = 1 PRINT N'PASS: ' + @AppPrincipal + N' can INSERT dbo.AuditLogs';
        ELSE BEGIN SET @fail += 1; PRINT N'FAIL: ' + @AppPrincipal + N' cannot INSERT dbo.AuditLogs (see scripts/009-grant-admin-audit-insert-template.sql)'; END
        IF ISNULL(@updUsers, 0) = 1 AND ISNULL(@insRoles, 0) = 1 AND ISNULL(@delRoles, 0) = 1 AND ISNULL(@insCust, 0) = 1 AND ISNULL(@updCust, 0) = 1
            PRINT N'PASS: ' + @AppPrincipal + N' has UPDATE Users, INSERT/DELETE UserRoles, INSERT/UPDATE Customers';
        ELSE BEGIN SET @warn += 1; PRINT N'WARN: ' + @AppPrincipal + N' lacks some of UPDATE Users / INSERT,DELETE UserRoles / INSERT,UPDATE Customers'; END
    END TRY
    BEGIN CATCH
        BEGIN TRY REVERT; END TRY BEGIN CATCH END CATCH; -- no-op when EXECUTE AS itself failed
        SET @warn += 1; PRINT N'NOT_VERIFIED: cannot impersonate ' + @AppPrincipal + N' (' + ERROR_MESSAGE() + N')';
    END CATCH
END

PRINT N'SUMMARY: failures=' + CONVERT(nvarchar(10), @fail) + N', warnings=' + CONVERT(nvarchar(10), @warn);
IF @fail > 0 PRINT N'NEXT: fix FAIL items (migration 010 / grant template 009), rerun; keep Security__Audit__RequireDurableAudit=false until PASS.';
ELSE IF @warn > 0 PRINT N'NEXT: review WARN/NOT_VERIFIED items; set @AppPrincipal to confirm permissions before enabling Security__Audit__RequireDurableAudit=true.';
ELSE PRINT N'NEXT: set Security__Audit__RequireDurableAudit=true for TAPortal Admin so changes without a durable audit row are rejected.';
PRINT N'VERIFY_STATUS=' + CASE WHEN @fail > 0 THEN N'FAIL' WHEN @warn > 0 THEN N'WARN' ELSE N'PASS' END;
