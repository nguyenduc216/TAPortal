# TAPortal Release Candidate Manifest

- FEATURE_ID: `TAPORTAL-PLATFORM-BLAZOR-MIGRATION`
- CHANGE_ID: `TAPORTAL-20261011-BLAZOR-RELEASE-001`
- Branch: `feat/compact-dynamic-navigation`
- Commit SHA: `1dbad77668943fd43ccb3508dd152ce150748f88`
- Build date: `2026-10-11`
- .NET SDK: `8.0.425`
- Target framework: `net8.0`
- Publish path: `D:\Working\T.A\Tech\Projects\TAPortal\sourceN\TAPortal\publish-TAPortal-release-candidate`
- Artifact tree SHA-256: `c43f0a96464d7ca9547e4fa7888f5aa17fd98739e3d1da3add81465f148ac50c`
- Main assembly SHA-256: `041C5046967BF38AF75E783C6D2510C6A939DA069EA78DF0F390B249115B4FAB`

## Verification

- Restore: PASS.
- Release build: PASS, 0 warnings/errors.
- Automated .NET/bUnit tests: PASS, 76/76.
- Anonymous Playwright on Kestrel: PASS, 6/6.
- `git diff --check`: PASS with the pre-existing appsettings line-ending warning.
- Publish contents: DLL, EXE, `web.config`, Blazor framework/runtime dependencies and static assets present.
- Credential scan: no connection string, password, admin secret or Playwright storage state found in the candidate; `appsettings.json` was intentionally excluded.

## Runtime configuration requirements

Supply configuration outside the artifact using IIS environment variables or an approved secret provider. At minimum provide `Database__ConnectionString` and the approved ASP.NET Core environment setting. Keep bootstrap disabled unless an explicitly approved secure bootstrap procedure exists. Do not put credentials in source, ZIP files, manifests, screenshots or logs.

## Known blockers

- No authorized authenticated Playwright storage state was available.
- Authenticated Dashboard/Profile/Users/Roles/Menus/Customers/Departments/Teams verification is BLOCKED.
- SignalR WebSocket and reconnect/restart recovery evidence is BLOCKED.
- IIS staging has not been exercised.
- SQL client reported negotiated TLS 1.0; production readiness requires TLS 1.2+ remediation/acceptance.

## Readiness decision

- READY FOR IIS STAGING DEPLOYMENT: **NO**.
- READY FOR PRODUCTION REPLACEMENT: **NO**.