# TA Portal Direct Folder Publish and SYS_ADMIN Verification

- Date: 2026-10-12
- FEATURE_ID: `TAPORTAL-SECURITY-ADMIN-AUTHORIZATION`
- CHANGE_ID: `TAPORTAL-20261012-ADMIN-DEPLOY-002`
- Branch: `feat/compact-dynamic-navigation`
- HEAD before task: `1dbad77`

## Scope

- Verify the existing server-side `SYS_ADMIN` authorization policy and Customers MVC/Blazor behavior.
- Build and test the latest source.
- Publish directly to the required IIS folder; ZIP was not used as a deployment source and no new ZIP was created.

## Files changed

- Added this code-history file.
- No application source changes were required during this task.

## Permission findings

- Source resolves Super Admin from active, non-deleted authenticated user membership in active, non-deleted role `Code='SYS_ADMIN'` with `IsSystem=1`.
- The resolver does not use username, display name, client claims, query strings or unverified cookies.
- Ordinary-user explicit DENY precedence remains intact; the approved Super Admin policy bypasses DENY only for trusted `SYS_ADMIN` membership.
- Customers create authorization is checked server-side for both MVC and Blazor, including the database write path.
- MVC and Blazor use the same `Customers.Index`/`Customers.Create` action resolution.
- The Customers toolbar warning is no longer rendered. The Create button remains disabled when unauthorized.
- No authenticated production test account was supplied. Therefore live production SYS_ADMIN membership and production Customers access were not claimed as PASS; verification was limited to source, seed, automated tests and local Playwright behavior.
- No role was created and no production database INSERT/UPDATE/DELETE/DDL was executed.

## Verification

- `dotnet restore TAPortal.sln`: PASS.
- `dotnet build TAPortal.sln -c Release --no-restore`: PASS, 0 warnings/errors.
- `dotnet test TAPortal.sln -c Release --no-build`: PASS, 86/86.
- Playwright `@customers-001`: PASS, 7/7 across MVC/Blazor and 1440/768/390 viewports.
- `git diff --check`: PASS, apart from existing line-ending normalization warnings.

## Direct publish

- Required publish path: `D:\Working\T.A\Tech\Projects\TAPortal\sourceN\TAPortal\publish-TAPortal-blazor`.
- Previous folder backup: `D:\Working\T.A\Tech\Projects\TAPortal\sourceN\publish-TAPortal-blazor-direct-backup-20261012-094234`.
- Release publish completed directly into the required folder.
- Final publish file count: 58.
- `TAPortal.Web.dll` SHA-256: `6F2AFE362C07E19402CBC42FBBBB5E00AFC04163F3B7F99B80DBD72CF608AB35`.
- Verified `TAPortal.Web.dll`, `TAPortal.Web.exe`, `web.config`, CSS, JS, logo and runtime dependencies.
- `appsettings.json` was preserved byte-for-byte; SHA-256 before/after: `4327504584B03D01F4AF2A43CB5BE5810B63878A443E5C79876AE9F499C0A537`.
- `appsettings.example.json` was not used as runtime configuration.
- No ZIP was created for this task.

## Git and blockers

- Branch remains `feat/compact-dynamic-navigation`, HEAD remains `1dbad77`.
- Working tree contains substantial pre-existing overlapping changes and a staged `src/TAPortal.Web/appsettings.json` deletion.
- This task only added the history file; no application source file required staging.
- No commit or push was made because the working tree cannot be safely committed without including or risking unrelated changes.
- IIS production deployment and live SYS_ADMIN account verification remain owner-controlled blockers.