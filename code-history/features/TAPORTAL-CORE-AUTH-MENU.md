# TAPORTAL-CORE-AUTH-MENU

## Purpose

Phase 6A establishes database-driven application authorization, FunctionAction mapping, and a user-specific hierarchical menu backend without implementing Employee/HR CRUD, Task, or a UI redesign.

## Baseline

The source before `TAPORTAL-20261010-CORE-AUTH-MENU-001` is the recovered architecture baseline. `PortalDb` already owned SQL access and effective-permission assembly at login, while `PermissionAuthorization` and `_Layout` still depended on stale cookie claims and a hard-coded `SYS_ADMIN` bypass.

## Current invariants

- Business authorization never grants access from a role name.
- Effective permissions are recomputed from the database with `User DENY > User ALLOW > Role ALLOW`.
- Permission changes take effect on the next authorization/menu request; there is no permission cache.
- Permission claims are no longer emitted into new authentication cookies. Role claims remain informational only.
- Unknown FunctionAction keys and unmapped actions fail closed.
- A FunctionAction is granted when the user has any active permission mapped to it.
- Menus with no `MenuPermissions` are visible to authenticated users by default.
- Protected menus require any configured effective permission.
- Non-navigable parents remain only when they contain an authorized descendant.
- DataScope remains separate and reserved for the later Employee phase.

## Extension points

- Use `PERM:<permission-code>` policies for direct database permission enforcement.
- Use `ACTION:<ui-or-endpoint-key>` policies when an endpoint should follow `FunctionActionPermissions` dynamically.
- Phase 6B may recursively render `PortalMenuNode.Children` at arbitrary depth without changing the query contract.
- Future administration writes for role, user override, and menu/action mappings should add structured `AuditLogs` entries in the same transaction.
