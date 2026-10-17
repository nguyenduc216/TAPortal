# TAPORTAL-20261015-CRM-PHASE1-018 — direct development database integration

FEATURE_ID: TAPORTAL-CRM-CUSTOMER-360

Status: PARTIAL. Owner authorized direct use of the existing TAPortalSys development database. A real connection to TAPortalSys succeeded; read-only permission/schema and aggregate data queries executed. No database was created and no SQL writes, migrations, fixture setup, or cleanup executed.

Preflight observed five CRM tables (Customers, CustomerEmployeeAssignments, CustomerBranchShares, CustomerServiceTeams, AuditLogs). The connection has SELECT database permission and INSERT permission on Customers. Aggregate counts: active/not-deleted customers 0; active users 1; active/not-deleted employees 0; companies 0; branches 0; teams 0; employee postings 0; relation rows 0; audit rows 0. Therefore no existing valid internal employee actor or customer/organizational target can exercise the nine service mutation paths. The existing user was not modified; no authentication bypass or credentials introduced.

Verification: focused CRM .NET tests 38 passed; full Web.Tests 135 passed; Release Web build 0 warnings/0 errors. These are offline results, not SQL-backed service tests. Authenticated Playwright storage state is unavailable. SQL-backed list/detail and all nine mutation paths, scope/permission matrix, overlap, RowVersion, audit rollback and concurrency remain NOT_RUN. The earlier anonymous browser check is not authenticated acceptance. No defects were confirmed by SQL-backed mutation testing and no IIS deployment was performed.

Next: provision an approved development employee identity and minimal organization hierarchy, create an identifiable new CRM customer fixture without modifying existing business records, then run service-path matrix and authenticated browser tests with verified cleanup. Never label script preparation or aggregate reads as mutation integration success.