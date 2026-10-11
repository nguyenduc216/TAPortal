# Blazor Migration Phase 2 — MVC Route Inventory

Feature: `TAPORTAL-PLATFORM-BLAZOR-MIGRATION`
Change: `TAPORTAL-20261011-BLAZOR-MIGRATION-002`

This inventory is planning/audit material only. No MVC module is migrated or removed by Phase 2. Existing controllers remain the route owners until component parity is separately verified.

| Route | Controller/action | View / model | Service and database dependencies | Authorization | JS/forms/API | Risk / priority | Parity test |
|---|---|---|---|---|---|---|---|
| `/account/login` | `AccountController.Login` GET/POST | `Views/Account/Login.cshtml`, `LoginVm` | `PortalDb.GetAuthUserAsync`, `PasswordService`, cookie middleware | Allow anonymous | Antiforgery POST, password toggle, loading JS | Cookie response boundary; HIGH | Invalid/valid credentials, cookie, return URL |
| `/account/profile` | `AccountController.Profile` GET | `Views/Account/Profile.cshtml`, `AuthUserSnapshot` | `PortalDb.GetAuthUserAsync` | `[Authorize]` | MVC navigation | Identity freshness; HIGH | Identity fields and unauthorized redirect |
| `/account/change-password` | `AccountController.ChangePassword` GET/POST | `Views/Account/ChangePassword.cshtml`, `ChangePasswordVm` | `PortalDb.UpdatePasswordAsync`, `PasswordService`, `PasswordPolicy` | `[Authorize]` | Antiforgery POST, validation, loading JS | Credential mutation; HIGH | Validation, hash update, re-login |
| `/users` | `UsersController.Index/Create` | `Views/Users/Index.cshtml`, `PortalUser`, `UserCreateVm` | `PortalDb.GetUsersAsync/CreateUserAsync/UsernameExistsAsync`, `PasswordService` | `ACTION:Users.Index/Create` | Antiforgery form, client loading | Permission and password rules; HIGH | List, validation, authorized create |
| `/roles` | `RolesController.Index` | `Views/Roles/Index.cshtml`, `PortalRole` | `PortalDb.GetRolesAsync` | `ACTION:Roles.Index` | MVC navigation | Read-only parity; MEDIUM | Authorized/denied list |
| `/menus` | `MenusController.Index` | `Views/Menus/Index.cshtml`, `PortalMenu` | `PortalDb.GetMenusAsync` | `ACTION:Menus.Index` | MVC navigation | Administration vs shell separation; MEDIUM | Authorized list and menu data |
| `/customers` | `CustomersController.Index` | `Views/Customers/Index.cshtml`, `CustomerRow` | `PortalDb.GetCustomersAsync` | `ACTION:Customers.Index` | Search/filter JS where present | Read-only parity; MEDIUM | Authorized list/search |
| `/hr/departments` | `DepartmentsController.Index/Create/Update/ToggleStatus/Delete` | `Views/Departments/Index.cshtml`, partial fields, Department VMs | `PortalDb` Department CRUD, `DepartmentRules`, RowVersion/soft delete | `ACTION:Departments.*` | Antiforgery POSTs, modal/form JS | Concurrency/dependency rules; HIGH | CRUD, duplicate, stale RowVersion, dependency delete |
| `/hr/teams` | `TeamsController.Index/Create/Update/ToggleStatus/Delete` | `Views/Teams/Index.cshtml`, partial fields, Team VMs | `PortalDb` Team CRUD/options, `TeamRules`, hierarchy/RowVersion/soft delete | `ACTION:Teams.*` | Antiforgery POSTs, department filter, modal/form JS | Hierarchy/cycle rules; HIGH | CRUD, parent/department/cycle/concurrency |

## Shared migration dependencies

- `PortalDb` remains the sole SQL owner; no new component SQL is permitted.
- `PermissionPolicyProvider`, `PermissionHandler`, and `FunctionActionHandler` remain authoritative.
- Login/logout should remain HTTP cookie endpoints unless a safe response-boundary design is proven.
- Blazor mutations must re-check authorization at the service/data boundary; hidden UI is not security.
- Existing `ta-portal.js` owns MVC DOM behavior and should only be reduced after equivalent component behavior is verified.
