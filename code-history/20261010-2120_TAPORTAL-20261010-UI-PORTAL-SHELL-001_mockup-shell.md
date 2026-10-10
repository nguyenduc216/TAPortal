# TAPORTAL-20261010-UI-PORTAL-SHELL-001 — Mockup-aligned portal shell

- Date: 2026-10-10
- Agent: Codex
- Project: T.A Portal
- Branch: `feat/compact-dynamic-navigation`
- FEATURE_ID: `TAPORTAL-UI-PORTAL-SHELL`
- CHANGE_ID: `TAPORTAL-20261010-UI-PORTAL-SHELL-001`

## Request

Implement Phase 6B as a UI-shell and design-system refinement on the completed Phase 6A authorization/menu engine, without implementing new business modules.

## Design reference

Primary named reference: `t.a_portal_mockup_tổng_thể_hệ_thống.png`. The image was not accessible in the workspace or attachment, so the written reference rules were applied and no pixel-comparison claim is made.

## Scope

Layout ownership, recursive database-driven sidebar, route ancestry, desktop collapse, mobile drawer, compact topbar, UI tokens/density, and visual proof on Dashboard, Users, Roles, Menus, Customers, Profile, Login, and Change Password.

## Out of scope

All new business CRUD/modules, authorization changes, raw menu queries, schema changes, dependency upgrades, SPA frameworks, real global search, and fabricated dashboard data.

## Architecture reused

Phase 6A `PortalDb.GetNavigationMenuTreeAsync` supplies the permission-filtered arbitrary-depth tree. Request-time database authorization, `ACTION:`/`PERM:` policies, DENY precedence, fail-closed action mapping, and lack of a role-name bypass remain unchanged.

## Files changed

- `src/TAPortal.Web/Views/Shared/_Layout.cshtml`
- `src/TAPortal.Web/Views/Shared/_Sidebar.cshtml`
- `src/TAPortal.Web/Views/Shared/_SidebarMenuNode.cshtml`
- `src/TAPortal.Web/Views/Shared/_Topbar.cshtml`
- `src/TAPortal.Web/wwwroot/css/ta-portal.css`
- `src/TAPortal.Web/wwwroot/js/ta-portal.js`
- `src/TAPortal.Web/Views/Home/Index.cshtml`
- `src/TAPortal.Web/Views/Roles/Index.cshtml`
- `src/TAPortal.Web/Views/Customers/Index.cshtml`
- `src/TAPortal.Web/Views/Account/Profile.cshtml`
- `code-history/features/TAPORTAL-UI-PORTAL-SHELL.md`
- this change history

Existing Users and Menus screens already used the compact table/search/icon patterns and were retained as proof consumers. Existing Login and Change Password behavior and styling were preserved.

## Changed UI sections

- Shell: clear partial ownership and compact responsive content outlet.
- Sidebar: recursive rendering, icon on every node, active ancestry, server-side route normalization, local collapsed preference, tooltips/titles, and mobile drawer.
- Topbar: disabled search placeholder, notification affordance, claim-backed identity, and preserved account actions.
- Design system: compact typography, tokens, 44px card headers, dense tables/forms, page header/actions, KPI/empty/profile patterns.
- Proof pages: operational dashboard hierarchy, dense role/customer tables, and structured profile identity/account cards.

## Verification

- Local HTTP smoke: `/Account/Login` returned 200 and included the T.A Portal title, versioned stylesheet reference, and login form.
- Browser visual verification: not performed because the available in-app browser provider was unavailable.
- Authenticated Dashboard/Users/Roles/Menus/Customers/Profile visual checks: not claimed.
- Static review confirmed desktop, laptop/tablet breakpoint, mobile drawer/backdrop, table containment, focus/ARIA, active ancestry, and collapsed-title behavior.

## Build

`dotnet build TAPortal.sln -c Release --no-restore`: PASS, 0 warnings, 0 errors.

## Tests

`dotnet test TAPortal.sln -c Release --no-build`: PASS, 18/18.

## Publish

`dotnet publish src/TAPortal.Web/TAPortal.Web.csproj -c Release --no-build -o publish-TAPortal`: PASS. DLL, deps/runtime configuration, CSS, and JavaScript artifacts were verified; the ignored output was not staged.

## Git result

`git diff --check`: PASS. The final commit/push SHA is reported in the delivery response because recording a commit's own SHA inside that commit is self-referential.

## Deployment notes

No database migration or seed is required. Deploy the Web publish output with the Phase 6A runtime/database contract already verified by the requester. Global search and notifications remain explicit presentation-only affordances.
