# TAPortal database bootstrap

Database engine: Microsoft SQL Server 2014+.
Database name: `TAPortal`.

## Database convention

All application tables are standardized under the `dbo` schema. Do not create application tables under `sys`, `auth`, `org`, `core`, or `audit` schemas.

Main common tables:

- Identity/security: `dbo.Users`, `dbo.Roles`, `dbo.Permissions`, `dbo.UserRoles`, `dbo.RolePermissions`, `dbo.UserPermissions`
- Organization: `dbo.Companies`, `dbo.Branches`, `dbo.Teams`, `dbo.UserBranches`, `dbo.UserTeams`
- Common UI/config: `dbo.Modules`, `dbo.Functions`, `dbo.Menus`, `dbo.MenuPermissions`, `dbo.Settings`, `dbo.NumberSequences`
- Data scope: `dbo.DataScopes`, `dbo.RoleDataScopes`, `dbo.UserDataScopes`
- Audit: `dbo.AuditLogs`, `dbo.LoginHistories`, `dbo.SystemLogs`

Common rules: `uniqueidentifier` + `NEWSEQUENTIALID()`, UTC with `SYSUTCDATETIME()`, soft-delete/audit columns on master tables, foreign keys without cascade delete, role/user permission model, data scopes `SELF/ASSIGNED/TEAM/BRANCH/COMPANY/CUSTOM`.

## Existing database migration

If previous scripts created tables in `auth`, `org`, `core`, or `audit`, run `001-create-common-schemas.sql` first. It transfers known legacy tables into `dbo` using `ALTER SCHEMA ... TRANSFER`, preserving table data, indexes, keys and foreign keys.

## Execution order

Run as SQL administrator in SSMS against database `TAPortal`:

1. `migrations/001-create-common-schemas.sql` - normalize legacy schemas to dbo
2. `migrations/002-create-auth-core.sql`
3. `migrations/003-create-org-core.sql`
4. `migrations/004-create-system-common.sql`
5. `migrations/005-create-data-scope.sql`
6. `migrations/006-create-audit-core.sql`
7. `seeds/001-seed-common-data.sql`
8. `scripts/001-create-mcp-readonly-user.sql` only if MCP SQL login/user has not already been created
9. `scripts/002-verify-common-database.sql`

The seed is idempotent and contains no passwords or secrets.

Expected verification result:

```text
COMMON DATABASE VERIFY: OK
```

The query section `LEGACY TABLES OUTSIDE DBO (SHOULD BE 0)` must return zero rows.

## MCP configuration

```text
Database__ConnectionString=Server=<sql-host>;Database=TAPortal;User Id=taportal_ai_reader;Password=<secret>;Encrypt=True;TrustServerCertificate=<true-or-false>
```

MCP endpoints:

```text
GET /health
MCP /mcp
```

`/health` only verifies that the MCP web process is reachable. To verify SQL connectivity, invoke MCP tool `DbPing`, then `DbListSchemas` or `DbListTables`.


## T.A Portal operational expansion (008-015)

Run these only after the existing 001-007 migrations and baseline seeds have completed successfully:

1. `008-navigation-hierarchy.sql`
2. `009-partner-tenant-foundation.sql`
3. `010-provider-billing-foundation.sql`
4. `011-banking-core.sql`
5. `012-payment-reconciliation.sql`
6. `013-invoice-core.sql`
7. `014-feature-navigation-permissions.sql`
8. `015-payment-audit-hardening.sql`
9. `016-sepay-bankhub-environments.sql`
10. `017-payment-allocation-procedures.sql`

Do not skip the order. Migration 009 backfills current Companies/Customers and current users into the T.A platform Partner. Migration 014 activates only feature menus whose read-only portal pages are included in the same release. Migration 015 preserves the many-IPN-to-one-bank-transaction audit trail and exposes `vw_PaymentRequestBalances`, where payment status is derived from active allocations rather than trusting a mutable paid amount. Migration 016 separates SePay SANDBOX/PRODUCTION connections, webhook configuration and sanitized provider API logs. Migration 017 adds atomic allocation/reversal procedures and prevents allocating more than the normalized bank transaction amount.

Before production execution, take a database backup and run the scripts against a staging copy first. Provider secrets must not be stored directly in `PartnerProviderConnections.ConfigJson`; use `SecretReference`.
