# TAPortal Login 500 / Branding / Deploy Report

## Environment
- Repository: nguyenduc216/TAPortal
- Branch: main
- Local source path: D:\Working\T.A\Tech\Projects\TAPortal\sourceN\TAPortal
- IIS Site: BLOCKED - appcmd cannot read IIS configuration due to insufficient permissions
- Application Pool: BLOCKED - IIS configuration unavailable
- Production physicalPath: BLOCKED - IIS configuration unavailable
- Production URL: https://portal.tuvangiaiphapta.vn

## Git
- Base commit: 838dfe24123e236911c2c4a74861534168f0c5bb
- Final commit: Not created; mandatory production/logo requirements are blocked
- origin/main commit: 838dfe24123e236911c2c4a74861534168f0c5bb
- Initial status: clean, main synchronized with origin/main after `git pull origin main`

## Root Cause
- Exact HTTP 500 cause: BLOCKED - production application logs/IIS logs/Event Viewer entries were not accessible from this session.
- Exception: Not obtained from production. Local prior startup with a read-only DB account failed during bootstrap with `The INSERT permission was denied on the object 'Users', database 'TAPortal', schema 'dbo'.`
- Stack trace summary: local read-only failure occurred in `TAPortal.Web.Services.PortalDb.EnsureAdminAsync`, called from `Program.cs`.
- SQL/query involved if applicable: dashboard query was tested directly against SQL Server and succeeded; it returned Customers=0, Users=1, ActiveUsers=1, Roles=1.

## Database
- Tables checked: dbo.Users, dbo.Roles, dbo.UserRoles, dbo.Permissions, dbo.RolePermissions, dbo.UserPermissions, dbo.Customers, dbo.Menus, dbo.Modules, dbo.Functions, dbo.Settings, dbo.AuditLogs.
- Migrations checked: migration files 001 through 007 exist under `database/migrations`.
- Seeds checked: seed files 001 through 003 exist under `database/seeds`.
- Database object verification: all listed core tables returned non-null OBJECT_ID values.
- Seed counts: Permissions=21, Menus=7, Roles=1, Users=1.
- Admin status: admin exists, NormalizedUsername=ADMIN, Status=ACTIVE, IsActive=1.
- SYS_ADMIN status: admin has SYS_ADMIN.

## Production Version Audit
- Source main version: 838dfe24123e236911c2c4a74861534168f0c5bb.
- Deployed version before fix: BLOCKED - IIS physicalPath could not be resolved.
- Whether production was OLD or CURRENT: BLOCKED.
- Evidence: `curl -I https://portal.tuvangiaiphapta.vn/Account/Login` returned HTTP 404 from Microsoft-HTTPAPI/2.0. `appcmd list site` failed with `Cannot read configuration file due to insufficient permissions`.

## Code Changes
- `src/TAPortal.Web/appsettings.json`: removed committed production DB credential and restored an empty connection string placeholder. Kept `Bootstrap:EnsureAdmin=false`.
- `src/TAPortal.Web/Services/PortalDb.cs`: changed connection string validation to reject null, empty, and whitespace values with the existing clear configuration error.

## Logo
- Source asset path: `src/TAPortal.Web/wwwroot/images/ta-logo.png`
- Source result: MISSING
- Publish asset path: `src/publish/portal-fresh-20260906-095505/wwwroot/images/ta-logo.png`
- Publish result: MISSING
- Production asset path: BLOCKED - IIS physicalPath unavailable
- Production URL: https://portal.tuvangiaiphapta.vn/images/ta-logo.png
- HTTP status: 404

## Favicon
- Source path: `src/TAPortal.Web/wwwroot/favicon.ico`, `src/TAPortal.Web/wwwroot/images/favicon-32x32.png`, `src/TAPortal.Web/wwwroot/images/apple-touch-icon.png`
- Source result: MISSING
- Publish path: `src/publish/portal-fresh-20260906-095505`
- Publish result: favicon files MISSING
- Production path: BLOCKED - IIS physicalPath unavailable
- Production URL: https://portal.tuvangiaiphapta.vn/favicon.ico
- HTTP status: 404

## Validation
- Restore result: `dotnet restore .\TAPortal.sln` passed.
- Build result: `dotnet build .\TAPortal.sln -c Release` passed with 0 warnings and 0 errors.
- Test result: `dotnet test .\TAPortal.sln -c Release --no-build` exited 0 with no output.
- git diff --check result: passed; Git reported CRLF normalization warnings for changed files.

## Publish
- Publish command: `dotnet publish "D:\Working\T.A\Tech\Projects\TAPortal\sourceN\TAPortal\src\TAPortal.Web\TAPortal.Web.csproj" -c Release -o "D:\Working\T.A\Tech\Projects\TAPortal\sourceN\TAPortal\src\publish\portal-fresh-20260906-095505"`
- Publish folder: `D:\Working\T.A\Tech\Projects\TAPortal\sourceN\TAPortal\src\publish\portal-fresh-20260906-095505`
- Important output files verified:
  - TAPortal.Web.dll: present, 190976 bytes
  - web.config: present, 556 bytes
  - wwwroot/css/ta-portal.css: present, 5515 bytes
  - wwwroot/images/ta-logo.png: missing
  - favicon.ico: missing
  - wwwroot/images/favicon-32x32.png: missing
  - wwwroot/images/apple-touch-icon.png: missing

## Deployment
- Deployment folder: BLOCKED - IIS physicalPath could not be determined.
- Deployment time: Not deployed.
- App pool recycle/restart result: Not performed.

## Production Smoke Test
- Login page HTTP status: 404
- admin login result: Not tested; login page unavailable and production root cause/logs inaccessible.
- redirect result: Not tested.
- dashboard HTTP status: Not tested.
- logo result: 404 for `/images/ta-logo.png`
- sidebar logo result: Not testable without authenticated production UI.
- favicon result: 404 for `/favicon.ico`

## Final Git Status
- final SHA: Not committed.
- origin/main SHA: 838dfe24123e236911c2c4a74861534168f0c5bb
- working tree status: modified source/report files pending review.

## Acceptance Criteria
- main is up to date before changes: PASS
- actual HTTP 500 root cause identified: FAIL
- HTTP 500 fixed: FAIL
- admin login works: FAIL
- post-login redirect works: FAIL
- dashboard returns HTTP 200: FAIL for production, PASS for direct SQL dashboard query
- dbo.Users exists: PASS
- dbo.Roles exists: PASS
- dbo.Customers exists: PASS
- admin account exists: PASS
- admin has SYS_ADMIN: PASS
- real T.A logo exists in source: FAIL
- real T.A logo exists in publish output: FAIL
- real T.A logo exists in production deploy folder: FAIL
- /images/ta-logo.png returns HTTP 200: FAIL
- login page shows real T.A logo: FAIL
- sidebar shows real T.A logo: FAIL
- favicon is configured: PARTIAL - layout links exist but point at missing logo image
- favicon asset exists in publish output: FAIL
- favicon asset exists in production: FAIL
- favicon URL returns HTTP 200: FAIL
- Release build passes: PASS
- git diff --check passes: PASS
- fresh publish succeeds: PASS
- production deployed into actual IIS physicalPath: FAIL
- correct app pool restarted/recycled: FAIL
- production smoke test passes: FAIL
- final code committed to main: FAIL
- origin/main equals local HEAD: PASS before changes; not applicable after uncommitted changes
- report Markdown created and committed: PARTIAL - report created, not committed

## Remaining Issues
- Production IIS site and physicalPath cannot be identified without IIS configuration permissions.
- Production logs/stack trace for the reported post-login HTTP 500 are not accessible from this session.
- Approved real T.A logo asset is missing from the repo/workspace; favicon assets cannot be derived safely.
- Public production URL currently returns HTTP 404 from Microsoft-HTTPAPI/2.0 for login, logo, and favicon.
- Deployment and app pool recycle were not performed because the actual IIS target could not be verified.
- No commit or push was made because mandatory acceptance criteria are failing.
