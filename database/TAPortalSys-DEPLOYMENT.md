# TAPortalSys deployment boundary

`TAPortal.Web` does not embed a database name. The database is selected exclusively by `Database:ConnectionString` (environment variable `Database__ConnectionString`). For production, that connection string must select `TAPortalSys`.

Historical migrations `001`–`052` retain their original `USE [TAPortal]` statements because they are the audited V1 chain and must not be silently retargeted. Do not run that historical chain against `TAPortalSys`.

Migration `053-core-dynamic-action-authorization.sql` is an additive Phase 6A reconciliation script. It deliberately contains no `USE` statement, checks its prerequisites, and must be run only by deployment tooling that:

1. connects explicitly to the intended `TAPortalSys` database;
2. verifies `DB_NAME()` before execution;
3. records the script hash/result in the release manifest;
4. runs the normal postflight checks afterward.

The Phase 6A implementation did not execute migration 053 and did not modify database data. The available read-only credential could inspect only the legacy `TAPortal` database and was denied access to `TAPortalSys`; therefore the owner must compare the target metadata with migration 053 before execution. If `FunctionActions` and `FunctionActionPermissions` already exist, validate their column contract against the application query before rollout.
