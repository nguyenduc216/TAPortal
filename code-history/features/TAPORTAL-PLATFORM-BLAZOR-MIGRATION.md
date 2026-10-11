# TAPORTAL-PLATFORM-BLAZOR-MIGRATION

## Purpose
Migrate the T.A Portal UI from MVC/Razor toward a .NET 8 Blazor Web App using Interactive Server while preserving PortalDb, TAPortalSys, cookie authentication, dynamic permissions, menu filtering, and existing business rules.

## Why
Component ownership and circuit-aware recovery/loading UX are required for the target platform without redesigning the verified domain and SQL layers.

## Architecture
- Old: ASP.NET Core MVC controllers and Razor views.
- Target: Blazor Web App with Interactive Server components, scoped UI state, and MVC retained only during staged parity cutover.
- Preserved owners: PortalDb, OperationalDb, PasswordService, Security/PermissionAuthorization, TAPortalSys.
- Authentication: `TA.Portal.Auth` cookie remains the identity boundary; login/logout HTTP endpoints remain safe MVC endpoints.
- Authorization: dynamic `PERM:` and `ACTION:` policies remain database-resolved. User DENY > User ALLOW > Role ALLOW remains unchanged.

## Components
`Components/App.razor`, `Routes.razor`, `Layout/MainLayout`, `Sidebar`, recursive `SidebarMenuNode`, `Topbar`, `ReconnectModal`, `Shared/GlobalLoadingOverlay`, and the initial Dashboard page were created. Sidebar consumes `PortalDb.GetNavigationMenuTreeAsync` and does not duplicate SQL authorization.

## UX architecture
`GlobalLoadingService` is scoped and reference-counted for overlapping operations. ReconnectModal is a blocking recovery surface; the current bridge offers retry/reload and is not claimed as browser-verified.

## Route migration map
`/` is now owned by the Blazor Dashboard component. Login, logout, profile, password change, administration, customer, department, and team MVC routes remain temporarily retained until each corresponding component is migrated and verified. No duplicate ownership was introduced for those routes.

## Constraints and deployment
No database schema or migration changes. Interactive Server requires persistent SignalR connections; IIS WebSockets should be enabled. Multi-node scale-out is not implemented and may require sticky sessions/Azure SignalR later.

## Ordered change registry
1. Add Razor Components and Interactive Server startup.
2. Add Blazor route/layout foundation and authentication-aware routing.
3. Reuse database-driven navigation in recursive components.
4. Add scoped global loading and reconnect UI foundations.
5. Migrate dashboard route.
6. Continue page parity migration before retiring MVC ownership.
7. Integration hardening: route/security audits, bUnit component rendering, repeatable Playwright scaffolding, staging/test-environment guidance, secure Git inventory/checkpoint, and active-account write revalidation.
8. Functional parity completion: Department/Team edit, Team parent selection, Roles/Menus retry states, component behavior tests, and staging-readiness documentation.
9. Local runtime verification: real Kestrel/Chromium multi-viewport checks, read-only database authentication-path validation, anonymous route evidence, and branded HTTP status handling.

## Phase 9 local runtime verification

`TAPORTAL-20261011-BLAZOR-MIGRATION-009` executed the application locally rather than relying on source inspection. Release Kestrel startup, responsive Chromium rendering, assets, JavaScript interaction, browser errors, anonymous authorization redirects, a synthetic invalid-login database read, and HTTP 404 behavior were tested. A blank default 404 was the only source defect found and was replaced with a branded anonymous status page that preserves the original status code.

The local SQL read succeeded but again negotiated TLS 1.0. No authorized Playwright storage state was available, so authenticated Blazor/SignalR/reconnect checks remain blocked without bypass. Local anonymous evidence is PASS; server staging and production cutover remain NO.

## Phase 8 functional parity

`TAPORTAL-20261011-BLAZOR-MIGRATION-008` completes the verified MVC edit surface for Departments and Teams on the Blazor trial routes. Both flows reuse existing validation models and `PortalDb` update methods, preserve row-version concurrency, recheck function-action permissions at write time, prevent duplicate submissions, sanitize database failures, and refresh only after successful persistence. Team parent options are department-scoped and exclude self in the UI while the existing serializable database transaction remains authoritative for department, self-parent, and cycle validation.

Roles and Menus now distinguish loading, authorization denial, empty, success, and retryable data failures. A narrow `IBlazorPortalService` contract leaves SQL ownership in `PortalDb` and enables real bUnit interaction tests without a database connection. The staging runbook and Playwright suite document secret injection, approved accounts/data cleanup, WebSocket/reconnect evidence, and opt-in writes. MVC routes and schema remain unchanged. Because no authorized staging account/environment is available, authenticated browser and reconnect evidence remain pending; status is PARTIAL and cutover remains NO.

## Phase 7 integration hardening

`TAPORTAL-20261011-BLAZOR-MIGRATION-007` audited all nine trial routes and every migrated write operation. bUnit now renders the actual reconnect, recursive menu and loading-overlay components; security regression contracts verify write-time identity/permission checks. Profile save now revalidates the current circuit identity and active account, while password UPDATE fails closed if the account is disabled/deleted between validation and mutation.

A credential-free Playwright suite covers anonymous fail-closed behavior and is prepared for authenticated route/refresh/multi-tab/permission checks through an external storage state. Staging provisioning, Git inventory and security remediation documents were added. Authenticated browser parity, reconnect interruption, approved CRUD writes and IIS staging evidence remain blocked, so route cutover remains NO.

## Phase 2 stabilization
Interactive Server registration and render modes were verified by Release build and publish output. The reconnect surface now uses the framework reconnect element/class contract and exposes retry/reload states without inventing a second circuit. Global loading remains scoped per circuit, is reference-counted with idempotent operation scopes, delays display to reduce flicker, and marshals updates through the component dispatcher. MVC modules remain unchanged pending parity migration.

## SQL connectivity reverification
`TAPORTAL-20261011-SQL-CONNECTIVITY-002` re-read the user-provided `Database:ConnectionString` without modifying it. TCP connectivity to `113.160.249.61:1433`, SqlClient parsing, independent `PortalDb` and `OperationalDb` opens, `SELECT 1`, and the read-only Dashboard query passed against `TAPortalSys`. The Dashboard/auth tables required by the current Blazor page were present. The checked legacy operational table names were not present, so those `OperationalDb` methods remain unverified against this schema. SqlClient reported a negotiated TLS 1.0 warning; no security configuration was changed. Anonymous HTTP/browser smoke passed for login and authorization redirects. Authenticated Dashboard, permission matrix, and reconnect scenarios remain blocked pending a valid test account and browser session evidence.

## Pre-migration validation (CHANGE_ID: TAPORTAL-20261011-BLAZOR-PRECHECK-004)
Read-only audit confirmed that both `PortalDb` and `OperationalDb` use the same `Database:ConnectionString` and target `TAPortalSys`. The configured `OperationalDb` queries reference `dbo.Partners`, `dbo.PartnerBankAccounts`, `dbo.BankTransactions`, and `dbo.InvoiceRequests`, but none of those tables exist in any schema found in `TAPortalSys`. No migration/DDL source artifact was found; ownership remains unresolved and no schema/data change was made.

Authentication logic was reviewed: active/non-deleted users are loaded by `PortalDb`, passwords are verified by `PasswordService`, cookies carry identity/roles, and permissions remain database-resolved. Bootstrap admin is disabled. One active user and one role mapping were observed without exposing identity or hash data, but no authorized test account or safe credential was provided. Authenticated Playwright checks are therefore `BLOCKED — Authorized test account required`; anonymous HTTP smoke is not authenticated PASS evidence.

SQL metadata was queried read-only. The database is online and `is_encrypted=False`; SqlClient again warned that TLS 1.0 was negotiated. SQL/network owners must remediate to TLS 1.2+ with certificate validation. No server/network setting was changed. Release build passed with 0 warnings/errors and the full suite passed 47/47. Status remains PARTIAL and readiness remains NO until account, schema ownership, TLS, IIS/staging, and secret-rotation blockers are resolved.

## Account/Profile Blazor trial (CHANGE_ID: TAPORTAL-20261011-BLAZOR-MIGRATION-004)
Added authenticated trial routes `/blazor/account/profile` and `/blazor/account/change-password` while retaining the MVC routes and production menu ownership. Profile editing is limited to display name, email, and phone; identity is resolved from the authenticated `NameIdentifier` claim and updates are scoped to the active account. Change Password reuses the existing view model, policy, PBKDF2 service, and database update logic without writing cookies from the SignalR circuit. Loading cleanup, duplicate-submit guards, generic database errors, and overposting protections were added. Release build passed with 0 warnings/errors and automated tests passed 50/50. Authenticated browser parity remains blocked pending an authorized test account; route cutover remains NO.

## Users, Roles and Menus trial (CHANGE_ID: TAPORTAL-20261011-BLAZOR-MIGRATION-005)
Added authenticated trial routes `/blazor/users`, `/blazor/roles`, and `/blazor/menus` without changing MVC ownership. Users reuses the existing create model, password policy/hash service, and server-derived creator identity; Roles reads the existing role projection; Menus reuses `GetNavigationMenuTreeAsync` and its permission-aware recursive tree. Function-action permissions are rechecked server-side, loading/error handling is generic, and duplicate user creation is guarded. No unsupported role/menu CRUD or user update/reset behavior was invented. Release build passed with 0 warnings/errors and tests passed 50/50. Authenticated browser parity remains blocked pending an authorized test account; route cutover remains NO.

## Customers and HR trial (CHANGE_ID: TAPORTAL-20261011-BLAZOR-MIGRATION-006)
MVC audit verified Customers as read-only list/search, Departments as CRUD/status/soft-delete, and Teams as CRUD/status/soft-delete with department/parent hierarchy validation. Added `/blazor/customers`, `/blazor/hr/departments`, and `/blazor/hr/teams` using existing PortalDb methods, models, dynamic action permissions, authenticated server identity, row-version handling, scoped loading, validation, empty/error states, and duplicate-submit guards. MVC route ownership, schema, data, production sidebar, and configuration remain unchanged. Phase 6 automated tests cover normalization/validation, hierarchy and cycle protection, write-model overposting surface, and trial route ownership. Authenticated browser parity and approved database writes remain blocked; cutover remains NO.
