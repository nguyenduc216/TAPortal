# Blazor Phase 7 route audit

Audit date: 2026-10-11. Production MVC routes remain unchanged; `/blazor/**` routes are trial owners.

| Route | Component / render mode | Server access | Data / states | Writes and ownership | Result |
|---|---|---|---|---|---|
| `/` | `Dashboard`, Interactive Server | `[Authorize]`; no function permission | `PortalDb.GetDashboardAsync`; initial null projection | Read-only; Blazor owns `/` | PARTIAL: no explicit error/empty surface |
| `/blazor/account/profile` | `AccountProfile`, Interactive Server | `[Authorize]`; active identity revalidated on save | Profile load, loading, missing/error state | Self-profile only; MVC `/account/profile` retained | PASS source contract; browser pending |
| `/blazor/account/change-password` | `AccountChangePassword`, Interactive Server | `[Authorize]`; current identity and active account checked | Validation, generic errors, duplicate guard/loading | Self-password only; active predicate at UPDATE | PASS source contract; browser pending |
| `/blazor/users` | `Users`, Interactive Server | `Users.Index`; `Users.Create` rechecked at write | List/loading/error/empty | Create only; MVC `/users` retained | PASS source contract; browser pending |
| `/blazor/roles` | `Roles`, Interactive Server | `Roles.Index` | List/loading/empty; caught DB error is not surfaced | Read-only; MVC `/roles` retained | PARTIAL: silent load error |
| `/blazor/menus` | `Menus`, Interactive Server | `Menus.Index`; menu filtering remains DB-owned | Recursive tree/loading/empty; caught DB error is not surfaced | Read-only; MVC `/menus` retained | PARTIAL: silent load error |
| `/blazor/customers` | `Customers`, Interactive Server | `Customers.Index` | List/search/loading/error/empty | Read-only; MVC `/customers` retained | PASS source contract; browser pending |
| `/blazor/hr/departments` | `HrDepartments`, Interactive Server | Index plus Create/Update/Delete rechecked per write | List/loading/error/empty; duplicate guard | Create/status/delete; RowVersion from server state; MVC route retained | PARTIAL: edit parity not implemented |
| `/blazor/hr/teams` | `HrTeams`, Interactive Server | Index plus Create/Update/Delete rechecked per write | Search/filter/loading/error/empty; duplicate guard | Create/status/delete; transactional hierarchy owner in PortalDb | PARTIAL: edit and parent-selection parity not implemented |

All components inherit `[Authorize]` through `_Imports.razor` and also declare it on routable pages. `App.razor` applies Interactive Server to `Routes`; no trial component claims an MVC production path. `GlobalLoadingOverlay` unsubscribes and cancels delayed work on disposal. Page components do not own unmanaged resources. Navigation remains database-driven and therefore currently points at production MVC routes until explicit cutover.
