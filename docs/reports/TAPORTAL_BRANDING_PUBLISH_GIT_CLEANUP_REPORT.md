# TAPortal Branding / Publish Git Cleanup Report

## Repository
- repository: nguyenduc216/TAPortal
- branch: main
- base commit: 838dfe24123e236911c2c4a74861534168f0c5bb
- final commit: pending at report creation time

## Initial Problem
- images folder missing or present: missing from `src/TAPortal.Web/wwwroot`
- logo missing or present: missing at `src/TAPortal.Web/wwwroot/images/ta-logo.png`
- favicon missing or present: missing at `src/TAPortal.Web/wwwroot/favicon.ico`, `src/TAPortal.Web/wwwroot/images/favicon-32x32.png`, `src/TAPortal.Web/wwwroot/images/favicon-192x192.png`, and `src/TAPortal.Web/wwwroot/images/apple-touch-icon.png`
- generated publish files appearing in Git status: `src/publish/` was untracked; no publish/bin/obj files were tracked by Git
- pre-existing local changes: `src/TAPortal.Web/appsettings.json`, `src/TAPortal.Web/Services/PortalDb.cs`, `docs/reports/TAPORTAL_LOGIN_500_LOGO_FAVICON_DEPLOY_REPORT.md`

## .gitignore
- old relevant rules: `bin/`, `obj/`, and many file-specific `publish/mcp/...` and `src/publish/portal/...` entries
- new rules: `**/bin/`, `**/obj/`, `publish/`, `src/publish/`, `**/publish/`, `**/publish-*/`
- confirmation publish/bin/obj are ignored:
  - `.gitignore:13:**/publish/ src/publish/portal/TAPortal.Web.dll`
  - `.gitignore:4:**/bin/ src/TAPortal.Web/bin/Release/net8.0/TAPortal.Web.dll`
  - `.gitignore:5:**/obj/ src/TAPortal.Web/obj/Release/net8.0/apphost.exe`

## Removed From Git Tracking
- None. `git ls-files "src/publish/**" "publish/**" "**/bin/**" "**/obj/**"` returned no tracked files.

## Logo
- approved asset source: `D:\Working\T.A\Tech\Projects\TAPortal\source\TAPortal.PortalWebApp\images\Logo_T.A.png`
- final source path: `src/TAPortal.Web/wwwroot/images/ta-logo.png`
- format: PNG
- dimensions: 4000x4000
- size: 228163 bytes

## Favicons
- favicon.ico:
  - source path: `src/TAPortal.Web/wwwroot/favicon.ico`
  - size: 8556 bytes
  - dimensions: source ICO copied from approved legacy T.A asset set
- favicon-32x32.png:
  - source path: `src/TAPortal.Web/wwwroot/images/favicon-32x32.png`
  - size: 852 bytes
  - dimensions: 32x32
- favicon-192x192.png:
  - source path: `src/TAPortal.Web/wwwroot/images/favicon-192x192.png`
  - size: 4225 bytes
  - dimensions: 192x192
- apple-touch-icon.png:
  - source path: `src/TAPortal.Web/wwwroot/images/apple-touch-icon.png`
  - size: 3935 bytes
  - dimensions: 180x180

## Razor Changes
- `_Layout.cshtml`: changed favicon markup to explicit 32x32 PNG, 192x192 PNG, ICO, and apple-touch icon assets with `asp-append-version`.
- `Login.cshtml`: no change required; it already uses `~/images/ta-logo.png`.
- Sidebar logo: no change required beyond adding the source asset; `_Layout.cshtml` already uses `~/images/ta-logo.png`.

## Build
- restore: PASS, `dotnet restore .\TAPortal.sln`
- build: PASS, `dotnet build .\TAPortal.sln -c Release`, 0 warnings, 0 errors
- test: PASS exit code 0, `dotnet test .\TAPortal.sln -c Release --no-build`
- git diff --check: PASS with CRLF normalization warnings only

## Publish Validation
- fresh publish folder: `D:\Working\T.A\Tech\Projects\TAPortal\deploy-temp\TAPortal.Web-20260906-101236`
- logo exists: PASS, `wwwroot/images/ta-logo.png`, 228163 bytes
- favicon exists: PASS
  - `wwwroot/images/favicon-32x32.png`, 852 bytes
  - `wwwroot/images/favicon-192x192.png`, 4225 bytes
  - `wwwroot/images/apple-touch-icon.png`, 3935 bytes
  - `wwwroot/favicon.ico`, 8556 bytes
- relevant output paths:
  - `wwwroot/css/ta-portal.css`, 5515 bytes
  - `TAPortal.Web.dll`, 192512 bytes
  - `web.config`, 556 bytes

## Git Validation
- files committed: pending at report creation time
- files intentionally ignored: `src/publish/**`, `publish/**`, `**/bin/**`, `**/obj/**`, `**/publish/**`, `**/publish-*/**`
- final SHA: pending at report creation time
- origin/main SHA: 838dfe24123e236911c2c4a74861534168f0c5bb before commit
- working tree status: pending commit

## Remaining Issues
- Real approved logo was found in the legacy TAPortal project workspace and copied into the new source tree.
- Deployment was not attempted because this task focused on source cleanup/build/publish validation and the IIS physicalPath remains unavailable without elevated IIS configuration access.
