# TAPORTAL-20261010-UI-PORTAL-SHELL-002 — Visual refinement

- Date: 2026-10-10
- Agent: Codex
- Project: T.A Portal
- Branch: `feat/compact-dynamic-navigation`
- FEATURE_ID: `TAPORTAL-UI-PORTAL-SHELL`
- CHANGE_ID: `TAPORTAL-20261010-UI-PORTAL-SHELL-002`
- Baseline: `a9057aac8237d044728853080a986cad9156bb6f`

## Request

Refine the live Phase 6B shell because the functional UI remained too flat and its sidebar, dashboard, and topbar did not visually match the approved direction. Preserve all Phase 6A authorization and Phase 6B shell architecture.

## Audit findings

- The configured Tabler Icons URL omitted the package's `dist/` directory and returned HTTP 404.
- The corrected pinned URL, `@tabler/icons-webfont@3.19.0/dist/tabler-icons.min.css`, returned HTTP 200 with the expected stylesheet payload.
- Menu markup already emitted icon elements and chevrons, but no glyph font loaded because of the failed asset.
- Database icon values may be either `ti-name` or `ti ti-name`; normalization now extracts one valid `ti-*` glyph class.
- Sidebar hierarchy existed structurally but root/child weight, guide rails, icon surfaces, counts, and open-state contrast were too subtle.
- Dashboard data was real but composition left large, visually unfinished areas.

## Scope

- Correct the pinned Tabler Icons stylesheet path.
- Normalize menu icon values without changing the menu query or authorization filter.
- Strengthen root/child hierarchy, chevrons, active/open states, and collapsed icon rail.
- Add dashboard and notification affordances to the topbar while retaining the account dropdown.
- Restyle existing KPI metrics and add account-only quick actions.
- Improve the activity empty state and system-health presentation using existing information only.

## Out of scope

Authorization or database changes, new menu seeds, business CRUD/modules, fake business metrics, a notification backend, global search, framework/library upgrades, and a new frontend stack.

## Files changed

- `src/TAPortal.Web/Views/Shared/_Layout.cshtml`
- `src/TAPortal.Web/Views/Shared/_SidebarMenuNode.cshtml`
- `src/TAPortal.Web/Views/Shared/_Topbar.cshtml`
- `src/TAPortal.Web/Views/Home/Index.cshtml`
- `src/TAPortal.Web/wwwroot/css/ta-portal.css`
- `code-history/features/TAPORTAL-UI-PORTAL-SHELL.md`
- this change history

## Authorization and data integrity

`PortalDb.GetNavigationMenuTreeAsync` remains the sole navigation source. Recursive rendering, server-side route activation, request-time permissions, DENY precedence, `ACTION:`/`PERM:` policies, and fail-closed behavior are unchanged. No database or C# service/controller code changed. Dashboard values remain the four existing `DashboardVm` metrics.

## Visual verification

- Old icon asset URL: confirmed HTTP 404.
- Corrected pinned icon asset URL: confirmed HTTP 200.
- Razor compilation confirms the normalized icon renderer and revised dashboard/topbar markup.
- Browser screenshot verification is not claimed because the available browser provider remained unavailable in this execution environment.

## Verification

- `dotnet build TAPortal.sln -c Release --no-restore`: PASS, 0 warnings, 0 errors.
- `dotnet test TAPortal.sln -c Release --no-build`: PASS, 18/18.
- `git diff --check`: PASS.
- `dotnet publish src/TAPortal.Web/TAPortal.Web.csproj -c Release --no-build -o publish-TAPortal`: PASS; DLL, deps/runtime configuration, and refined CSS artifacts verified.

## Deployment notes

No database action is required. The deployed environment must be able to reach jsDelivr for the pinned Tabler core and icon assets, consistent with the existing shell dependency model.
