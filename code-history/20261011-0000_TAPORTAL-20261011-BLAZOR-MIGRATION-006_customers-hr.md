# TAPORTAL-20261011-BLAZOR-MIGRATION-006

- FEATURE_ID: `TAPORTAL-PLATFORM-BLAZOR-MIGRATION`
- CHANGE_ID: `TAPORTAL-20261011-BLAZOR-MIGRATION-006`

## MVC audit and implementation

Customers currently exposes only a read-only `Index` projection with client-side search; no unsupported customer CRUD/details/status behavior was invented. Departments exposes list, search, create, update, status toggle, and soft delete. Teams exposes list, search/department filter, create, update, status toggle, and soft delete. Existing `PortalDb` remains the SQL owner and preserves row-version, dependency, active-department, same-department parent, and cycle validation.

Added authenticated Interactive Server trial routes `/blazor/customers`, `/blazor/hr/departments`, and `/blazor/hr/teams`. The components reuse existing models/rules, dynamic function-action permissions, authenticated server identity, scoped loading, validation, empty/error states, duplicate-submit guards, and server-provided row versions. MVC routes, schema, data, production sidebar ownership, and configuration remain unchanged.

## Matrix

| Module | MVC | Blazor | Automated Tests | Browser Parity | Cutover |
|---|---|---|---|---|---|
| Dashboard | Existing | Implemented | Existing | Pending | No |
| Profile | Existing | Implemented | Existing | Pending | No |
| Change Password | Existing | Implemented | Existing | Pending | No |
| Users | Existing | Implemented | Existing + Phase 5 audit | Pending | No |
| Roles | Existing | Implemented | Existing + Phase 5 audit | Pending | No |
| Menus | Existing | Implemented | Existing + Phase 5 audit | Pending | No |
| Customers | Existing read-only list/search | Phase 6 | Validation/route/security contracts | Blocked: no authorized account | No |
| HR Departments | Existing CRUD/status/delete | Phase 6 | Validation/route/security contracts | Blocked: no authorized account | No |
| HR Teams | Existing CRUD/status/delete/hierarchy | Phase 6 | Validation/hierarchy/route/security contracts | Blocked: no authorized account | No |

## Validation and status

Release build passed with 0 warnings/errors. Authenticated browser parity and approved non-production database writes remain blocked because no authorized test account was supplied. Status: `PARTIAL — Implementation complete, authenticated browser parity pending.` Route cutover: **NO**.