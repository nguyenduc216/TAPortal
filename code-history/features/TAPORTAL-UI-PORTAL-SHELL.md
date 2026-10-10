# TAPORTAL-UI-PORTAL-SHELL

## Purpose

Provide a compact, professional application shell and a small reusable UI vocabulary for T.A Portal without adding business modules or changing authorization semantics.

## Design source of truth

The approved `t.a_portal_mockup_tổng_thể_hệ_thống.png` is the named primary reference. It was not present in the repository or supplied attachments during CHANGE 001, so implementation follows the written visual rules: navy navigation, blue-gray canvas, white compact cards, restrained teal/blue accents, dense typography, and Tabler icons. No claim of pixel-level mockup comparison is made.

## Architecture baseline

Phase 6A feature `TAPORTAL-CORE-AUTH-MENU` at `5a6d791e148112385970502987a1538ac92fb5f9` remains authoritative for request-time authorization and menu filtering. UI code consumes `PortalDb.GetNavigationMenuTreeAsync`; it does not query raw menus or decide permissions.

## Layout owners

- `_Layout.cshtml`: document, authenticated/anonymous shell boundary, notifications, content outlet, and assets.
- `_Sidebar.cshtml`: obtains the already-authorized Phase 6A menu tree and owns navigation chrome.
- `_SidebarMenuNode.cshtml`: recursive, arbitrary-depth menu rendering and server-side route activation.
- `_Topbar.cshtml`: visual search placeholder, notifications affordance, current identity, and account menu.

## Sidebar architecture

Every node is rendered by the same recursive partial. Route paths are normalized without query strings or trailing slashes by the server request path. A matching descendant marks and opens every ancestor. JavaScript only expands/collapses branches, manages the mobile drawer, and persists desktop collapsed presentation; it never stores or grants authorization state.

## Topbar architecture

The topbar uses existing display-name and role claims, derives fallback initials without a database query, and preserves Profile, Change Password, and Logout. Global search is visibly disabled and labelled as forthcoming; no fake search backend exists.

## CSS and design-token rules

`ta-portal.css` remains the sole application stylesheet. Existing numbered ownership sections are retained and Phase 6B extends them with normalized color, typography, radius, shell-width, and density tokens. Reusable classes are introduced only where used: page header/actions, stat cards, empty state, profile hero/information list, and responsive shell controls.

## JavaScript ownership

`ta-portal.js` continues to own progress, loading states, filtering, password helpers, tooltips, and pageshow reset. Phase 6B adds presentation-only sidebar collapse, submenu toggles, mobile open/close, Escape dismissal, and localStorage persistence.

## Responsive and accessibility rules

- Desktop/laptop use a fixed 244px sidebar or a persisted 72px icon rail.
- Tablet/mobile use an off-canvas drawer with dismissible backdrop; content never retains sidebar margin.
- Tables scroll inside their cards; page actions wrap or expand on narrow screens.
- Navigation exposes labels to assistive technology, `aria-current`, `aria-expanded`, `aria-controls`, focus-visible states, accessible icon buttons, and titles in collapsed mode.

## Known constraints

- The primary mockup image was unavailable for direct visual comparison.
- Browser automation was unavailable in the execution environment. The local login endpoint passed an HTTP markup smoke check; authenticated pages were not visually inspected in a browser.
- Search and notification buttons are shell affordances only.
- No Employee, HR master, Task, Contract, Payment, E-Invoice, Payroll, Attendance, KPI, Customer 360, or reporting functionality is included.

## Ordered change registry

1. `TAPORTAL-20261010-UI-PORTAL-SHELL-001` — split shell ownership, recursive authorized navigation, active ancestry, persisted collapse/mobile behavior, compact topbar/design tokens, and proof-page refinements.
2. `TAPORTAL-20261010-UI-PORTAL-SHELL-002` — repair the Tabler Icons webfont asset path, normalize seeded icon classes, strengthen sidebar hierarchy/active presentation, enrich the topbar, and give the real-metric dashboard denser visual composition.
