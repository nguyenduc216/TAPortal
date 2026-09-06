# TAPortal Production Deploy / HTTP500 Final Report

## Git
- repository: nguyenduc216/TAPortal
- branch: main
- base commit: e7567230840e403cdf99789fe073bf71df24e05b
- final commit: pending at report creation time
- origin/main SHA: e7567230840e403cdf99789fe073bf71df24e05b before final commit
- initial working tree: clean
- pull result: `git pull origin main` returned `Already up to date.`

## IIS
- Site: BLOCKED - unavailable
- binding: BLOCKED - unavailable
- app pool: BLOCKED - unavailable
- app pool identity: BLOCKED - unavailable
- physicalPath: BLOCKED - unavailable
- appcmd command: `%windir%\system32\inetsrv\appcmd.exe list site`
- appcmd error: `Cannot read configuration file due to insufficient permissions`
- WebAdministration command: `Import-Module WebAdministration; Get-Website`
- WebAdministration error: `Process should have elevated status to access IIS configuration data.`
- required permission level: elevated Administrator permission on the IIS server

## Production Version Before
- OLD/CURRENT: BLOCKED - IIS physicalPath could not be identified
- evidence: public `GET https://portal.tuvangiaiphapta.vn/Account/Login` returned HTTP 200, but deployed files could not be inspected without IIS physicalPath access

## HTTP 500
- reproduce steps:
  - GET `/Account/Login`
  - POST `/Account/Login` with `admin` / `Admin@123`
  - production returned HTTP 500
  - local reproduction against the same TAPortal SQL Server with the read-only account returned HTTP 500 before the fix
- exact exception: `Microsoft.Data.SqlClient.SqlException (0x80131904): The UPDATE permission was denied on the object 'Users', database 'TAPortal', schema 'dbo'.`
- stack trace summary: `TAPortal.Web.Services.PortalDb.TouchLoginAsync(Guid userId)` called from `TAPortal.Web.Controllers.AccountController.Login(LoginVm vm, String returnUrl)`
- root cause: successful login attempted to update `dbo.Users.LastLoginAt`, but the configured DB principal `taportal_ai_reader` does not have UPDATE permission on `dbo.Users`.
- secondary issue: production exception handler path `/home/error` had no corresponding `HomeController.Error` action/view, so the exception handler could produce an additional 404 handling failure.
- fix:
  - `TouchLoginAsync` now logs SQL permission error 229 for the optional last-login update and allows authentication to continue.
  - `HomeController.Error` and `Views/Home/Error.cshtml` were added as a safe production error page.

## Bootstrap
- EnsureAdmin behavior before: committed config had `Bootstrap:EnsureAdmin=false`, but `Program.cs` defaulted to true if the setting was missing.
- EnsureAdmin behavior after: `Program.cs` uses `GetValue<bool>("Bootstrap:EnsureAdmin")`, so the default is false unless explicitly enabled.
- production setting: should remain false for the read-only production DB account.

## Database
- dbo.Users: exists
- dbo.Roles: exists
- dbo.Customers: exists
- dashboard query result: CustomerCount=0, UserCount=1, ActiveUserCount=1, RoleCount=1
- admin status: admin exists, NormalizedUsername=ADMIN, Status=ACTIVE, IsActive=1
- SYS_ADMIN status: admin has SYS_ADMIN

## Branding
- logo source path: `src/TAPortal.Web/wwwroot/images/ta-logo.png`
- favicon source paths:
  - `src/TAPortal.Web/wwwroot/images/favicon-32x32.png`
  - `src/TAPortal.Web/wwwroot/images/favicon-192x192.png`
  - `src/TAPortal.Web/wwwroot/images/apple-touch-icon.png`
  - `src/TAPortal.Web/wwwroot/favicon.ico`
- publish paths:
  - `wwwroot/images/ta-logo.png`: present
  - `wwwroot/images/favicon-32x32.png`: present
  - `wwwroot/images/favicon-192x192.png`: present
  - `wwwroot/images/apple-touch-icon.png`: present
  - `wwwroot/favicon.ico`: present
- production paths: BLOCKED - IIS physicalPath unavailable
- production HTTP status before deploy:
  - `/Account/Login`: 200
  - `/images/ta-logo.png`: 404
  - `/images/favicon-32x32.png`: 404
  - `/favicon.ico`: 404

## .gitignore
- cleaned broad rules: `bin/`, `obj/`, `**/bin/`, `**/obj/`, `publish/`, `src/publish/`, `**/publish/`, `**/publish-*/`
- removed redundant rules: file-specific publish entries for `publish/mcp/**` and `src/publish/portal/**`
- tracked artifact check result: `git ls-files "src/publish/**" "publish/**" "**/bin/**" "**/obj/**"` returned no files
- ignore verification: `git check-ignore -v src/publish/test/TAPortal.Web.dll` matched `.gitignore:13:**/publish/`

## Build
- restore: PASS, `dotnet restore .\TAPortal.sln`
- build: PASS, `dotnet build .\TAPortal.sln -c Release`, 0 warnings, 0 errors
- test: PASS, `dotnet test .\TAPortal.sln -c Release --no-build`, exit code 0
- git diff --check: PASS with CRLF normalization warnings only

## Publish
- publish folder: `D:\Working\T.A\Tech\Projects\TAPortal\deploy-temp\TAPortal.Web-20260906-102536`
- file verification:
  - `TAPortal.Web.dll`: present, 196096 bytes
  - `web.config`: present, 556 bytes
  - `wwwroot/css/ta-portal.css`: present, 5515 bytes
  - `wwwroot/images/ta-logo.png`: present, 228163 bytes
  - `wwwroot/images/favicon-32x32.png`: present, 852 bytes
  - `wwwroot/images/favicon-192x192.png`: present, 4225 bytes
  - `wwwroot/images/apple-touch-icon.png`: present, 3935 bytes
  - `wwwroot/favicon.ico`: present, 8556 bytes

## Deploy
- backup path if created: not created
- deployed path: not deployed
- deployment timestamp: not deployed
- app pool recycle result: not performed
- reason: IIS physicalPath and app pool could not be identified without elevated IIS access; deployment to a guessed folder is prohibited by the task.

## Production Smoke Test
- login page: PASS before deploy, HTTP 200
- admin login: FAIL before deploy, HTTP 500
- redirect: FAIL before deploy
- dashboard: not reached in production before deploy
- logo: FAIL before deploy, HTTP 404
- favicon: FAIL before deploy, HTTP 404
- local verification after fix: PASS for admin login, redirect to `/`, dashboard HTTP 200, logo HTTP 200, favicon HTTP 200

## Final Git
- final SHA: pending at report creation time
- origin/main SHA: pending final push
- working tree state: pending commit

## Acceptance Criteria
- .gitignore cleaned: PASS
- publish/bin/obj ignored: PASS
- no generated artifacts tracked: PASS
- IIS site identified: FAIL
- app pool identified: FAIL
- physicalPath identified: FAIL
- deployed version before recorded: FAIL
- real HTTP 500 exception captured if still reproducible: PASS
- root cause fixed: PASS locally, production pending deployment
- startup admin bootstrap production-safe: PASS
- dashboard SQL succeeds: PASS
- release build passes: PASS
- tests pass: PASS
- fresh publish succeeds: PASS
- logo included in publish: PASS
- favicon included in publish: PASS
- production deployed to actual physicalPath: FAIL
- app pool restarted: FAIL
- login page returns 200: PASS
- admin login succeeds: FAIL before deploy
- redirect succeeds: FAIL before deploy
- dashboard returns 200: FAIL before deploy
- logo URL returns 200: FAIL before deploy
- favicon URL returns 200: FAIL before deploy
- sidebar logo visible: FAIL before deploy
- browser favicon visible: FAIL before deploy
- final code committed to main: pending
- origin/main == local HEAD: pending
- production corresponds to final HEAD: FAIL
- final report committed: pending

## Remaining Issues
- Elevated Administrator access is required to read IIS configuration and determine the actual production physicalPath/app pool.
- Production deployment was not performed because the target folder cannot be guessed.
- Production still needs deployment from the final fixed build before the HTTP 500 and branding asset 404s can be marked resolved in production.
