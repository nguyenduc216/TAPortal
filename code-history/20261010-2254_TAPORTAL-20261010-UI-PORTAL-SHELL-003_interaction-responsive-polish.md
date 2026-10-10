# TAPORTAL-20261010-UI-PORTAL-SHELL-003 — Interaction and responsive polish

- Date: 2026-10-10
- Agent: Codex
- Project: T.A Portal
- Branch: `feat/compact-dynamic-navigation`
- FEATURE_ID: `TAPORTAL-UI-PORTAL-SHELL`
- CHANGE_ID: `TAPORTAL-20261010-UI-PORTAL-SHELL-003`
- Baseline: `a3c8e3bfd06c0185a77df9955b3d56c0b5dd3dd8`

## Request

Apply interaction and responsive polish derived from live screenshots after CHANGE 002, focusing on deterministic sidebar alignment, smooth submenu behavior, dashboard feedback, a functional notification empty state, and compact mobile composition.

## Audit

- Sidebar links reserved a guessed `3.75rem` right padding while count and chevron were independently absolutely positioned. Long labels could therefore displace or visually detach controls.
- Closed submenus used `display:none`, preventing reveal animation.
- KPI cards and system rows had little or no feedback on pointer devices.
- The notification indicator implied unread data even though no notification source exists, and the button had no behavior.
- Bootstrap's `col-sm-6` becomes full-width below 576px, creating four vertically stacked KPI cards.
- The disabled search shell, dashboard shortcut, notification, and avatar competed for narrow topbar width.

## Implementation

- Menu rows now use deterministic grid columns. Links internally align icon, ellipsized label, and count; chevrons occupy a dedicated contained column.
- Non-navigable groups render as accessible toggle buttons instead of `href="#"` anchors.
- Recursive children use a grid-row reveal wrapper with opacity and chevron transitions.
- Added fast/normal/slow motion and easing tokens used by navigation, KPI cards, quick actions, system rows, and topbar controls.
- Added pointer-only KPI lift/icon/orb feedback, system-row surface feedback, and quick-action icon/chevron motion.
- The bell now opens a Bootstrap-compatible, keyboard-accessible empty-state dropdown. The unread dot remains in markup but hidden unless future data adds `is-visible`.
- Mobile KPI cards form a 2×2 grid below 576px with reduced height, padding, icon/value sizes, and hidden secondary notes.
- Mobile header, quick strip, topbar, search, dashboard shortcut, notification panel, and avatar spacing were compacted at 576px and 400px breakpoints.
- Reduced-motion explicitly removes transforms and submenu transitions in addition to the existing global duration override.

## Architecture and security

The authorized `PortalMenuNode` tree, recursive rendering, server route activation, collapse preference, Phase 6A permission semantics, database schema, and backend services are unchanged. No business data or notification records were fabricated.

## Files changed

- `src/TAPortal.Web/Views/Shared/_SidebarMenuNode.cshtml`
- `src/TAPortal.Web/Views/Shared/_Topbar.cshtml`
- `src/TAPortal.Web/wwwroot/css/ta-portal.css`
- `code-history/features/TAPORTAL-UI-PORTAL-SHELL.md`
- this change history

`ta-portal.js` required no change: its existing delegated submenu toggle updates `is-open` and `aria-expanded`, while Tabler's existing Bootstrap bundle owns dropdown click-outside, Escape, and keyboard behavior.

## Responsive verification

Static rule review covered 1920/1366 desktop and laptop behavior, the existing 991px tablet drawer, 768px content behavior, 390px mobile composition, and the 360px/400px compact topbar. The 2×2 KPI rule retains 50% columns without page-level overflow. Browser screenshot verification is not claimed because the available computer-use state contained no browser provider.

## Verification

- `dotnet build TAPortal.sln -c Release --no-restore`: PASS, 0 warnings, 0 errors.
- `dotnet test TAPortal.sln -c Release --no-build`: PASS, 18/18.
- `git diff --check`: PASS.
- `dotnet publish src/TAPortal.Web/TAPortal.Web.csproj -c Release --no-build -o publish-TAPortal`: PASS; DLL, deps/runtime configuration, CSS, and JavaScript artifacts verified.

## Deployment notes

No database or configuration action is required. Deploy the normal Web publish output. The notification panel intentionally remains an empty shell until a real backend exists.
