# TAPORTAL-HR-MASTER-DATA

## Purpose

Establish production-ready HR master-data patterns for Department and Team, followed by JobTitle and Specialization in later changes. Employee remains outside the current scope.

## Current architecture

ASP.NET Core MVC/Razor uses `PortalDb` as its single SQL owner. Phase 6A request-time `ACTION:` policies and permission-filtered database navigation remain authoritative. Phase 6B supplies the compact shell, table, modal, responsive, icon, motion, and accessibility patterns.

## Data model

`dbo.Departments` stores the department master. `dbo.Teams` belongs to a required Department and may reference another nondeleted Team in the same Department as its parent. Both masters store canonical uppercase globally unique nondeleted codes, descriptive/sort/status fields, audit metadata, soft-delete state, and `RowVersion`.

Team hierarchy supports arbitrary depth. Self-parent, cross-Department parent, and recursive cycles are rejected by transactional application validation; migration and verification reject incompatible existing data. Foreign keys and a self-parent check protect the relationships at database level, while recursive cycle prevention remains in `PortalDb` because it is not expressible as a simple check constraint.

## Authorization model

- `HR.DEPARTMENT.VIEW` → `Departments.Index`
- `HR.DEPARTMENT.CREATE` → `Departments.Create`
- `HR.DEPARTMENT.UPDATE` → `Departments.Update` and the status endpoint through the same action policy
- `HR.DEPARTMENT.DELETE` → `Departments.Delete`
- `HR.TEAM.VIEW` → `Teams.Index`
- `HR.TEAM.CREATE` → `Teams.Create`
- `HR.TEAM.UPDATE` → `Teams.Update` and the status endpoint through the same action policy
- `HR.TEAM.DELETE` → `Teams.Delete`

Permissions are resolved through Modules → Functions → FunctionActions → FunctionActionPermissions → Permissions. No role-name application bypass exists. SYS_ADMIN receives the four permissions through `RolePermissions` during migration.

## Routes

- `GET /hr/departments`
- `POST /hr/departments/create`
- `POST /hr/departments/update`
- `POST /hr/departments/toggle-status`
- `POST /hr/departments/delete`
- `GET /hr/teams`
- `POST /hr/teams/create`
- `POST /hr/teams/update`
- `POST /hr/teams/toggle-status`
- `POST /hr/teams/delete`

## DB owner

`PortalDb` owns Department and Team reads/writes, duplicate detection, hierarchy validation, optimistic mutations, dependency-aware soft delete, parameter binding, and audit-user propagation.

## UI pattern

Each permission-aware page resolves action capabilities once. Team adds a Department filter and Department-aware parent choices. Department and Team use the shared `ta-crud-modal` treatment: normal fade, no Tabler background blur, an application backdrop opacity of `.18`, clear dialog elevation, and responsive mobile sizing.

## Business invariants

- Code is required, trimmed, uppercased, limited to 50 characters, and unique among nondeleted rows.
- Name is required and limited to 250 characters.
- Sort order is 0–9999.
- All SQL input is parameterized.
- Database uniqueness remains authoritative under concurrent create/update.

## Soft-delete rules

Delete sets `IsDeleted=1`, `IsActive=0`, and deletion/update audit metadata. It never physically deletes. It refuses deletion when compatible `Teams.DepartmentId` or `EmployeeAssignments.DepartmentId` references exist; absent future tables/columns are tolerated.

Team deletion additionally refuses nondeleted child Teams and future `EmployeeAssignments.TeamId` references. This preserves Department dependency protection after Teams becomes a concrete table.

## Concurrency rule

Edit, status change, and delete submit Base64-encoded original `RowVersion`. SQL predicates include `Id`, `RowVersion`, and `IsDeleted=0`; zero affected rows produce a clear reload-and-retry conflict message.

## Dependencies

- `TAPORTAL-CORE-AUTH-MENU` (Phase 6A)
- `TAPORTAL-UI-PORTAL-SHELL` (Phase 6B)
- TAPortalSys additive migration line beginning at 053

## Ordered change registry

1. `TAPORTAL-20261010-HR-MASTER-DATA-001` — Department schema, authorization/menu reconciliation, production CRUD, soft delete, rowversion concurrency, verification, and baseline tests.
2. `TAPORTAL-20261010-HR-MASTER-DATA-002` — Team master, same-Department acyclic hierarchy, dynamic authorization/menu, production CRUD, and shared light CRUD modal baseline correction.
