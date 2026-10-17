# TA Portal Admin Permission Root Cause and Functional Gap Audit

- Date: 2026-10-12
- FEATURE_ID: `TAPORTAL-SECURITY-ADMIN-AUTHORIZATION`
- CHANGE_ID: `TAPORTAL-20261012-ADMIN-RBAC-003`
- Branch: `feat/compact-dynamic-navigation`
- Commit/Push: not performed; working tree has overlapping changes and a staged appsettings deletion.

## Executive finding

The production account reviewed was `admin` / `T.A Administrator`, UserId `3894FE22-4CCD-4DD5-8D84-C1C610022AE2`. Read-only SQL verified:

- User is active, not deleted, and has status `ACTIVE`.
- The user has role `SYS_ADMIN`; the role is active, not deleted, and `IsSystem=1`.
- Production has an active Customers view mapping: `Customers.Index` / `customers.view` -> `CRM.CUSTOMERS.VIEW`.
- Production has no active `Customers.Create` action mapping and no returned `CRM.CUSTOMERS.CREATE` action mapping.

Therefore the exact root cause is a production permission metadata gap: `HasFunctionActionAsync("Customers.Create")` fails closed because the required active FunctionAction/FunctionActionPermissions mapping is absent. The UI correctly disables Create. This is not a username issue, stale cookie conclusion, or source/DLL authorization defect. The existing SYS_ADMIN bypass applies after a permission/action is resolved; it does not authorize an unknown or unmapped action.

No production INSERT, UPDATE, DELETE, DDL, role assignment, or permission grant was executed.

## Authorization trace

Authenticated principal -> database UserId -> active `SYS_ADMIN` membership -> `GetEffectivePermissionsAsync` -> `HasFunctionActionAsync("Customers.Create")` -> active FunctionActions mapping -> Customers Create UI/server write check.

Source and publish both contain the current resolver and Customers implementation. The resolver requires active, non-deleted system role membership and the action resolver intentionally fails closed for unmapped actions. MVC and Blazor both independently recheck Customers.Index/Create before writing.

## Required owner-approved production change

Review, approve, and execute the existing review-only script `database/migrations/20261012_CUSTOMERS-001_create-action-review.sql` in the approved production change process. It creates metadata only when safe, grants no user or role assignment, and must be followed by read-only verification and an authenticated acceptance test. Do not manually bypass authorization by username.

## Customers acceptance status

- SYS_ADMIN membership: PASS by read-only production SQL.
- Customers.Index: PASS by read-only production SQL.
- Customers.Create effective permission: BLOCKED/FAIL in production because mapping is absent.
- Create button enabled: BLOCKED in production; disabled behavior is correct for the observed metadata.
- Form validation, duplicate-submit protection, server-side write authorization, refresh, and export: IMPLEMENTED and covered by automated/local customer harness evidence; not authenticated IIS production acceptance.
- Unauthorized denial: IMPLEMENTED and covered by automated tests; not production acceptance.
- No production create/delete was attempted.

## Functional gap matrix

| Module | Status | Evidence / gap | Recommended next change |
|---|---|---|---|
| Dashboard | IMPLEMENTED / UNVERIFIED | MVC dashboard backend/view exists; no authenticated IIS acceptance evidence in this task | `TAPORTAL-20261012-ADMIN-RUNTIME-004` |
| Customers | PARTIAL / BLOCKED | MVC and Blazor CRUD-create/export code exists; production Create action metadata is missing | `TAPORTAL-20261012-CUSTOMERS-ACTION-METADATA-004` |
| HR Departments | IMPLEMENTED / UNVERIFIED | Existing MVC/service/schema code is present; no authenticated IIS acceptance evidence | `TAPORTAL-20261012-HR-MODULES-004` |
| HR Teams | IMPLEMENTED / UNVERIFIED | Existing MVC/service/schema code is present; no authenticated IIS acceptance evidence | `TAPORTAL-20261012-HR-MODULES-004` |
| Users | IMPLEMENTED / UNVERIFIED | MVC list/create, validation and database write path exist; no authenticated IIS acceptance evidence | `TAPORTAL-20261012-ADMIN-RUNTIME-004` |
| Roles | PARTIAL | MVC listing exists; complete role/permission administration acceptance is not evidenced | `TAPORTAL-20261012-ROLE-MANAGEMENT-004` |
| Menus | IMPLEMENTED / UNVERIFIED | Menu UI/backend and permission filtering exist; no authenticated IIS acceptance evidence | `TAPORTAL-20261012-ADMIN-RUNTIME-004` |
| Account Profile | IMPLEMENTED / UNVERIFIED | Profile read/update path exists; no authenticated IIS acceptance evidence | `TAPORTAL-20261012-ACCOUNT-SECURITY-004` |
| Change Password | IMPLEMENTED / UNVERIFIED | Password service/controller path exists; no authenticated IIS acceptance evidence | `TAPORTAL-20261012-ACCOUNT-SECURITY-004` |

Counts for the nine requested menu functions: DONE `0` (no authenticated IIS production acceptance), IMPLEMENTED_UNVERIFIED `6`, PARTIAL `2`, NOT_IMPLEMENTED `0`, BLOCKED `1` (Customers production Create acceptance). These classifications distinguish missing production metadata from missing application code; no unrequested capability is treated as a migration defect.

## Verification

- Release build: PASS, 0 warnings/errors.
- Existing full automated test result: PASS, 86/86 (from prior verified task state).
- Existing Customers Playwright `@customers-001`: PASS, 7/7 (local MVC/Blazor and responsive coverage; not IIS production).
- `git diff --check`: PASS apart from existing line-ending normalization warnings.
- Production database audit: PARTIAL/BLOCKED for full acceptance; read-only identity/action evidence obtained, but no write test was allowed and no authenticated IIS session was supplied.

## Publish and deployment

- Required publish path remains `D:\Working\T.A\Tech\Projects\TAPortal\sourceN\TAPortal\publish-TAPortal-blazor`.
- Existing publish is present and its previously recorded DLL SHA-256 is `6F2AFE362C07E19402CBC42FBBBB5E00AFC04163F3B7F99B80DBD72CF608AB35`.
- No code fix was required for this root cause; no republish was performed.
- Existing `appsettings.json` preservation record remains valid; no configuration was replaced.
- IIS deployment/replacement was not performed.

## Remaining blockers

1. Owner approval and controlled execution of the review-only Customers action metadata change.
2. Authenticated IIS acceptance using an approved account, including Create, validation, export, and unauthorized denial.
3. Safe selective commit/push is blocked by overlapping working-tree changes and staged `src/TAPortal.Web/appsettings.json` deletion. No force push and no unsafe commit.