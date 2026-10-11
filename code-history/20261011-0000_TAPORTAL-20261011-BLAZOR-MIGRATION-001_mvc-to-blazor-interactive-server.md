# T.A Portal MVC to Blazor Interactive Server

- date: 2026-10-11
- agent: Codex
- project: TAPortal
- branch: feat/compact-dynamic-navigation
- starting SHA: 4a644bcd92d7b7ede82860902404d3498facc923
- FEATURE_ID: TAPORTAL-PLATFORM-BLAZOR-MIGRATION
- CHANGE_ID: TAPORTAL-20261011-BLAZOR-MIGRATION-001

## Request and scope
Introduce the .NET 8 Blazor Web App/Interactive Server foundation and begin staged UI migration. Out of scope: HR features JobTitle, Specialization, Employee, Task, Contract, Payment, Invoice; schema redesign; migration cleanup; permission redesign.

## Old/new architecture
MVC controllers/Razor views are the old UI. Razor Components with Interactive Server now own `/` and the new shell/dashboard foundation. MVC remains temporarily for un migrated routes and authentication boundary.

## Preservation
PortalDb, OperationalDb, PasswordService, cookie settings, dynamic ACTION/PERM policy provider/handlers, FunctionActions, permission precedence, SQL schema, RowVersion and soft-delete business owners were not rewritten.

## Authentication and authorization
Cookie authentication remains `TA.Portal.Auth`, HttpOnly, Lax, sliding, eight hours. Existing login/logout endpoints remain MVC HTTP endpoints because cookie headers must be written before an interactive circuit is established. No claims-based permission snapshot or SYS_ADMIN bypass was added.

## Route migration table
| Route | Current owner | Change |
|---|---|---|
| `/` | Blazor Dashboard | migrated in this change |
| `/account/login` | MVC AccountController | retained HTTP cookie boundary |
| `/account/profile` | MVC | retained pending parity |
| `/account/change-password` | MVC | retained pending parity |
| `/users`, `/roles`, `/menus`, `/customers` | MVC | retained pending parity |
| `/hr/departments`, `/hr/teams` | MVC | retained pending parity |

## Files and services
Created `Components/**`, `Services/GlobalLoadingService.cs`, and `wwwroot/css/taportal-blazor.css`. No MVC files were retired because parity is not complete. MVC infrastructure retained to prevent broken routes and premature duplicate ownership.

## Reconnect/loading
ReconnectModal foundation provides blocking retry/reload actions. Browser circuit-loss verification was not available in this run. GlobalLoadingService is scoped, reference-counted, accessible through GlobalLoadingOverlay, and reduced-motion CSS is included.

## Database
SQL/Migration: NONE. TAPortalSys unchanged. Legacy migrations were not executed.

## Verification
- build: `dotnet build TAPortal.sln -c Release --no-restore` PASS
- test: `dotnet test TAPortal.sln -c Release --no-build` PASS, 44/44
- git diff --check: PASS (pre-existing appsettings line-ending warning only)
- publish: not run
- runtime/browser/reconnect smoke: not run; no claim made

## Deployment and rollback
Enable IIS WebSockets and use the .NET 8 Hosting Bundle. Interactive Server introduces persistent SignalR connections; scale-out is not implemented. Rollback is a source/publish rollback to `4a644bcd92d7b7ede82860902404d3498facc923`; no database rollback is needed.