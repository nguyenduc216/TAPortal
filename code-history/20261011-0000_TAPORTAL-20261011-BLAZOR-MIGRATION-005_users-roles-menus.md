# TAPORTAL-20261011-BLAZOR-MIGRATION-005

- FEATURE_ID: `TAPORTAL-PLATFORM-BLAZOR-MIGRATION`
- CHANGE_ID: `TAPORTAL-20261011-BLAZOR-MIGRATION-005`

## Audit and parity

The MVC audit found only `Users.Index/Create`, `Roles.Index`, and `Menus.Index` actions. Existing MVC views/services do not provide user update, reset password, role CRUD/permission assignment, or menu CRUD/order/edit actions. Those operations were not invented in the trial migration. MVC routes and ownership remain unchanged.

## Implementation

- Added `/blazor/users`, `/blazor/roles`, and `/blazor/menus` as authenticated Interactive Server trial routes.
- Users preserves MVC list/create behavior, uses `UserCreateVm`, `PasswordPolicy`, `PasswordService`, server-derived creator identity, and no client-editable id/hash/status fields.
- Roles reads the existing role projection only.
- Menus reuses `PortalDb.GetNavigationMenuTreeAsync`, including active/visible filtering, permission filtering, ordering, and recursive tree construction.
- Function-action permissions are rechecked against the current authenticated identity at operation time. Loading scopes, duplicate-submit protection, and generic database errors are used.
- MVC routes, schema, data, cookie authentication, and production menu ownership were not changed.

## Parity matrix

| MVC action | View/model | Trial implementation | Permission | Coverage |
|---|---|---|---|---|
| `Users.Index` | Users view / `PortalUser` list | `/blazor/users` list | `Users.Index` | Build + automated suite; authenticated browser blocked |
| `Users.Create` | POST / `UserCreateVm` | `/blazor/users` create form | `Users.Create` rechecked server-side | Existing validation/password tests; database write/browser blocked |
| `Roles.Index` | Roles view / `PortalRole` list | `/blazor/roles` list | `Roles.Index` | Build; authenticated browser blocked |
| `Menus.Index` | Menus view / authorized tree | `/blazor/menus` recursive read-only tree | `Menus.Index` plus existing menu resolution | Existing tree/auth semantics; authenticated browser blocked |

No MVC JavaScript behavior or write action exists for these modules. Role permissions, menu editing, user updates, reset password, filtering, and pagination remain out of scope until an MVC parity source is added.

## Validation

- Release build: PASS, 0 warnings/errors.
- Automated tests: PASS, 50/50.
- Authenticated browser/runtime parity: BLOCKED; no authorized test account.
- No migration, production deployment, schema or appsettings change.

## Status

`PARTIAL — Implementation complete, authenticated parity pending.`

Route cutover: **NO**.