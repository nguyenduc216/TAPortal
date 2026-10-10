# TAPORTAL-20261010-CORE-AUTH-MENU-001 — Dynamic authorization and menu backend

## Scope

- Reused `PortalDb` as the sole database-access owner.
- Removed the `SYS_ADMIN` role-name authorization bypass.
- Moved permission checks from login-time cookie snapshots to request-time database resolution.
- Added `ACTION:` policy support backed by `FunctionActions` and `FunctionActionPermissions`.
- Added a database-filtered, arbitrary-depth `PortalMenuNode` tree.
- Protected Customers with `CRM.CUSTOMERS.VIEW`.
- Added an additive SQL contract for missing FunctionAction tables and documented the `TAPortalSys` deployment boundary.
- Added focused menu-tree tests and Web publish to CI.

## Decisions

- No permission cache is used: assignment changes take effect on the next request.
- Multiple permission mappings for one menu/action use ANY semantics.
- Menus without permission mappings remain visible to authenticated users; backend endpoint policies remain authoritative.
- Profile and ChangePassword remain available to every authenticated user.
- Historical `USE [TAPortal]` migrations were not rewritten.
- Migration 053 was not executed because the available read-only login cannot open `TAPortalSys`.

## Out of scope

Employee/HR CRUD, Task, HR data filtering, recursive UI redesign, full authorization administration UI, and production deployment.
