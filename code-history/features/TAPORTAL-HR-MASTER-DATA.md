# TAPORTAL-HR-MASTER-DATA

## Purpose

Establish production-ready HR master-data patterns for Department first, followed by Team, JobTitle, and Specialization in later changes. Employee is explicitly outside this feature's first change.

## Current architecture

ASP.NET Core MVC/Razor uses `PortalDb` as its single SQL owner. Phase 6A request-time `ACTION:` policies and permission-filtered database navigation remain authoritative. Phase 6B supplies the compact shell, table, modal, responsive, icon, motion, and accessibility patterns.

## Data model

`dbo.Departments` stores `Id`, canonical uppercase `Code`, `Name`, optional `Description`, `SortOrder`, active/deleted state, creation/update/deletion audit metadata, and `RowVersion`. A filtered unique index protects nondeleted codes.

## Authorization model

- `HR.DEPARTMENT.VIEW` → `Departments.Index`
- `HR.DEPARTMENT.CREATE` → `Departments.Create`
- `HR.DEPARTMENT.UPDATE` → `Departments.Update` and the status endpoint through the same action policy
- `HR.DEPARTMENT.DELETE` → `Departments.Delete`

Permissions are resolved through Modules → Functions → FunctionActions → FunctionActionPermissions → Permissions. No role-name application bypass exists. SYS_ADMIN receives the four permissions through `RolePermissions` during migration.

## Routes

- `GET /hr/departments`
- `POST /hr/departments/create`
- `POST /hr/departments/update`
- `POST /hr/departments/toggle-status`
- `POST /hr/departments/delete`

## DB owner

`PortalDb` owns Department list, duplicate detection, create, optimistic update, status update, dependency-aware soft delete, parameter binding, and audit-user propagation.

## UI pattern

One permission-aware page resolves action capabilities once, provides client-side code/name search, compact responsive table, create/edit forms, status control, and an explicit soft-delete confirmation modal. Backend policies remain mandatory regardless of button visibility.

## Business invariants

- Code is required, trimmed, uppercased, limited to 50 characters, and unique among nondeleted rows.
- Name is required and limited to 250 characters.
- Sort order is 0–9999.
- All SQL input is parameterized.
- Database uniqueness remains authoritative under concurrent create/update.

## Soft-delete rules

Delete sets `IsDeleted=1`, `IsActive=0`, and deletion/update audit metadata. It never physically deletes. It refuses deletion when compatible `Teams.DepartmentId` or `EmployeeAssignments.DepartmentId` references exist; absent future tables/columns are tolerated.

## Concurrency rule

Edit, status change, and delete submit Base64-encoded original `RowVersion`. SQL predicates include `Id`, `RowVersion`, and `IsDeleted=0`; zero affected rows produce a clear reload-and-retry conflict message.

## Dependencies

- `TAPORTAL-CORE-AUTH-MENU` (Phase 6A)
- `TAPORTAL-UI-PORTAL-SHELL` (Phase 6B)
- TAPortalSys additive migration line beginning at 053

## Ordered change registry

1. `TAPORTAL-20261010-HR-MASTER-DATA-001` — Department schema, authorization/menu reconciliation, production CRUD, soft delete, rowversion concurrency, verification, and baseline tests.
