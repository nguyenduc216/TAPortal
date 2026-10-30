-- TAPORTAL-SYSTEM-AUDIT-CHG-20261028T173600Z-62724B — AUD-25 TEMPLATE ONLY. NOT_EXECUTED.
-- DBA review required. Replace the placeholder with the database user of the TAPortal Admin application
-- (never the read-only DbMcp user, never the Customer Portal user). Every statement is commented out on purpose.
-- Least privilege: INSERT only — the application never reads, updates or deletes audit rows.
-- Note: a principal with no permission on dbo.AuditLogs cannot see it (OBJECT_ID returns NULL), so the app would
-- treat the table as missing. Enable Security__Audit__RequireDurableAudit=true after scripts/007 reports PASS.
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
-- Equivalent required positive guard: DB_NAME() = 'TAPortalSys'. No USE statement.
-- GRANT INSERT ON dbo.AuditLogs TO [<TAPortal_Admin_app_db_user>];
PRINT N'009-grant-admin-audit-insert-template.sql: template only, nothing executed';
