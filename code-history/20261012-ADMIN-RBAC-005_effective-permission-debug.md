# SYS_ADMIN Effective Permission Debug

- Date: 2026-10-12
- FEATURE_ID: `TAPORTAL-SECURITY-ADMIN-AUTHORIZATION`
- CHANGE_ID: `TAPORTAL-20261012-ADMIN-RBAC-005`
- Branch: `feat/compact-dynamic-navigation`
- HEAD at inspection: `1dbad77668943fd43ccb3508dd152ce150748f88`

## Root cause

The production metadata was correct, but `PortalDb.GetEffectivePermissionsAsync` did not treat active `SYS_ADMIN` membership as an allow source. Its deny-exclusion branch recognized SYS_ADMIN, while its allow branch required either `UserPermissions.ALLOW` or an active `RolePermissions` row. Consequently `CRM.CUSTOMERS.CREATE` was absent from the effective permission set even though the action mapping existed.

`HasFunctionActionAsync` then correctly failed closed because its mapped permission was not effective. MVC and Blazor correctly propagated that result to `CanCreate` / `_canCreate` and disabled the button.

## SQL read-only evidence

- Database: `TAPortalSys`.
- `admin` UserId: `3894FE22-4CCD-4DD5-8D84-C1C610022AE2`; active, not deleted, `ACTIVE`.
- Active role: `SYS_ADMIN`, `IsSystem=1`, active, not deleted.
- Active `CUSTOMERS` function: FunctionId `41E246F3-A4C4-F111-9C4A-509A4C52AB53`.
- Active action: ActionId `5D56A961-11D6-4179-88E3-6491244453DB`, `Code=CREATE`, `EndpointKey=Customers.Create`, `UiKey=customers.create`.
- Active permission: PermissionId `B9507219-AA08-4D50-9F14-AD9FA60A6041`, `CRM.CUSTOMERS.CREATE`.
- One active action mapping was returned.
- Read-only effective-permission SQL returned `CRM.CUSTOMERS.CREATE` only when the SYS_ADMIN allow branch was included; the original resolver query omitted it.
- No production writes, grants, role changes, or metadata changes were executed.

## Fix

Changed `GetEffectivePermissionsAsync` so active, non-deleted authenticated SYS_ADMIN membership is an explicit allow source, while preserving:

- active account checks;
- explicit DENY precedence behavior already documented for ordinary users;
- active/non-deleted role and permission predicates;
- fail-closed behavior for missing or unmapped actions;
- server-side authorization on the MVC and database write paths.

Changed files:

- `src/TAPortal.Web/Services/PortalDb.cs`
- `tests/TAPortal.Web.Tests/SqlArtifactContractTests.cs`

## End-to-end trace

`NameIdentifier` -> active user lookup -> active SYS_ADMIN membership -> effective permission set -> `HasFunctionActionAsync(userId,"Customers.Create")` -> active FunctionAction mapping -> `ViewData["CanCreate"]` (MVC) / `_canCreate` (Blazor) -> button state.

Both MVC and Blazor use the exact `Customers.Create` key and do not hard-code permission enablement. The Blazor component refreshes permission state on load and before save. Cookie authentication is used only to establish the authenticated principal; permission decisions query the database by the server-side user ID.

## Verification

- Focused authorization/customer/SQL tests: PASS, `43/43`.
- Full automated .NET tests: PASS, `89/89`.
- Release build: PASS, 0 warnings/errors.
- Customers Playwright: not rerun in this task; no authenticated IIS browser session was available.
- `git diff --check`: attempted; existing LF/CRLF normalization warnings in unrelated working-tree files remain.

## IIS/artifact status

- Source Release DLL SHA-256 after fix: `8F990C13B2BC36BB508A4CC2062D42D915F93285E4940DB37FEE4072C7F31FAA`.
- Existing publish DLL SHA-256: `6F2AFE362C07E19402CBC42FBBBB5E00AFC04163F3B7F99B80DBD72CF608AB35`.
- Hashes differ; the existing publish folder does not contain this resolver fix.
- No publish or IIS deployment was performed. Owner can compare the IIS DLL with:
  `Get-FileHash 'D:\path\to\IIS\TAPortal.Web.dll' -Algorithm SHA256`.
- `appsettings.json` was not modified; no production restart was performed.

## Git

No commit or push was performed. The working tree contains overlapping unrelated changes and a staged `src/TAPortal.Web/appsettings.json` deletion; selective staging is not currently safe without owner isolation.

## Remaining blockers

1. Publish this source fix to the approved local publish folder only after selective artifact handling, if staging requires it.
2. Owner-controlled IIS deployment/restart and authenticated acceptance.
3. Confirm deployed IIS DLL hash matches the fixed Release/publish artifact.
4. Run Customers Playwright against the intended staged runtime; production PASS is not claimed from local tests.