# TAPortal Canonical Blazor Publish Manifest

- FEATURE_ID: `TAPORTAL-PLATFORM-BLAZOR-MIGRATION`
- CHANGE_ID: `TAPORTAL-20261011-BLAZOR-PUBLISH-001`
- Source branch: `feat/compact-dynamic-navigation`
- Source commit: `1dbad77668943fd43ccb3508dd152ce150748f88`
- Build date: `2026-10-11`
- .NET SDK: `8.0.425`
- Canonical publish directory: `D:\Working\T.A\Tech\Projects\TAPortal\sourceN\TAPortal\publish-TAPortal`
- Backup directory: `D:\Working\T.A\Tech\Projects\TAPortal\sourceN\publish-TAPortal-backup-20261011-182354`
- Output file count: `56`
- Artifact tree SHA-256: `23a6c79c02ff0374351791ec88ae9c54c720af4dcaa64e0960b2f1ccce845e91`
- `TAPortal.Web.dll` SHA-256: `041C5046967BF38AF75E783C6D2510C6A939DA069EA78DF0F390B249115B4FAB`

## Verification

- Restore: PASS.
- Release build: PASS, 0 warnings/errors.
- Automated tests: PASS, 76/76.
- Anonymous Playwright on current Kestrel source: PASS, 6/6.
- Publish to canonical directory: PASS.
- Backup before overwrite: PASS; 54 files copied.
- Existing canonical `appsettings.json`: preserved byte-for-byte from backup.
- Required DLL, EXE, deps, runtimeconfig, web.config, CSS, JavaScript, logo and icon assets: present.
- Blazor framework script is served by the Blazor framework endpoint, not as a physical `wwwroot\_framework\blazor.web.js` file.
- Playwright storage state/admin secret marker: not found.

## Configuration warning

The canonical directory retains its pre-existing `appsettings.json` by explicit user-safety policy. It contains local database configuration and must not be copied to IIS as a secret-bearing deployment artifact. Before IIS use, remove/exclude that file from the handoff and provide `Database__ConnectionString` through IIS environment variables or an approved secret provider.

## Known blockers

- Authenticated Playwright, permission matrix, SignalR WebSocket and reconnect/restart evidence remain BLOCKED.
- SQL client previously reported negotiated TLS 1.0; SQL/network remediation is required.
- IIS staging has not been exercised.

## Readiness

- READY TO COPY TO IIS STAGING: **NO** until the canonical configuration is externalized and staging/authenticated/WebSocket checks are completed.
- READY TO REPLACE PRODUCTION: **NO**.