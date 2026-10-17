# TAPortal Canonical Blazor Publish

- FEATURE_ID: `TAPORTAL-PLATFORM-BLAZOR-MIGRATION`
- CHANGE_ID: `TAPORTAL-20261011-BLAZOR-PUBLISH-001`
- Date: 2026-10-11
- Source commit: `1dbad77668943fd43ccb3508dd152ce150748f88`
- Canonical output: `D:\Working\T.A\Tech\Projects\TAPortal\sourceN\TAPortal\publish-TAPortal`

## Work performed

- Confirmed `publish-TAPortal` as the canonical Web publish directory from repository structure and existing artifact contents. Phase/candidate directories were not used.
- Ran restore, Release build, automated tests, `git diff --check`, and anonymous Playwright against current Kestrel source.
- Backed up the existing canonical output before publishing.
- Published `src/TAPortal.Web/TAPortal.Web.csproj` in Release configuration directly into the canonical directory.
- Restored the user's existing canonical `appsettings.json` byte-for-byte after publish; it was not overwritten.
- Did not change source configuration, database schema, production SQL, IIS or server state.

## Results

- Build: PASS, 0 warnings/errors.
- Tests: PASS, 76/76.
- Playwright anonymous runtime: PASS, 6/6.
- Publish: PASS, 56 files in canonical output.
- Backup: `D:\Working\T.A\Tech\Projects\TAPortal\sourceN\publish-TAPortal-backup-20261011-182354`.

## Handoff restrictions

The canonical output still contains the pre-existing `appsettings.json` because preserving user configuration was required. Do not copy that file to IIS when it contains credentials. Externalize `Database__ConnectionString` and other secrets first. Authenticated, SignalR/reconnect, IIS staging and SQL TLS verification remain pending.