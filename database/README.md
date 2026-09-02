# TAPortal database bootstrap

Database engine: Microsoft SQL Server 2019+ / Azure SQL compatible T-SQL where supported.
Database name: `TAPortal`.

## Common schemas

- `auth`: users, roles, permissions, direct user permissions, data scopes.
- `org`: companies, branches, teams and user assignments.
- `sys`: database-driven modules/functions/menus/settings/number sequences.
- `audit`: audit logs, login history and system logs.
- `crm`: reserved for CRM/customer functions; customer tables are intentionally not part of common bootstrap.

Common rules: `uniqueidentifier` + `NEWSEQUENTIALID()`, UTC with `SYSUTCDATETIME()`, soft-delete/audit columns on master tables, FK `NO ACTION`, role/user permission model, data scopes `SELF/ASSIGNED/TEAM/BRANCH/COMPANY/CUSTOM`.

## First-time execution order

Run as a SQL administrator in SSMS, against database `TAPortal`:

1. `migrations/001-create-common-schemas.sql`
2. `migrations/002-create-auth-core.sql`
3. `migrations/003-create-org-core.sql`
4. `migrations/004-create-system-common.sql`
5. `migrations/005-create-data-scope.sql`
6. `migrations/006-create-audit-core.sql`
7. `seeds/001-seed-common-data.sql`
8. `scripts/001-create-mcp-readonly-user.sql` (only if not already executed; replace password placeholder first)
9. `scripts/002-verify-common-database.sql`

All migrations/seeds are designed to be safe to re-run for initial/bootstrap deployment. Do not put production secrets in Git.

## MCP configuration

Set machine/process environment variable:

```text
Database__ConnectionString=Server=<sql-host>;Database=TAPortal;User Id=taportal_ai_reader;Password=<secret>;Encrypt=True;TrustServerCertificate=<true-or-false>
```

MCP endpoints:

```text
GET /health
MCP /mcp
```

A successful `/health` proves the web process is reachable. To prove database connectivity as well, invoke MCP tool `DbPing` and then `DbListSchemas` or `DbListTables`.
