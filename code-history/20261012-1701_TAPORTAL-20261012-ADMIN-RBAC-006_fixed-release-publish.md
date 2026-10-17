# TAPortal SYS_ADMIN Fixed Release Publish

- FEATURE_ID: `TAPORTAL-SECURITY-ADMIN-AUTHORIZATION`
- CHANGE_ID: `TAPORTAL-20261012-ADMIN-RBAC-006`
- Date: 2026-10-12
- Branch: `feat/compact-dynamic-navigation`
- HEAD: `1dbad77668943fd43ccb3508dd152ce150748f88`

## Source verification

The existing ADMIN-RBAC-005 fix was verified in `src/TAPortal.Web/Services/PortalDb.cs`. The effective permission SQL includes active, non-deleted system `SYS_ADMIN` membership as an explicit allow source. No SQL source or authorization policy was changed for this publish.

## Build and tests

- Release build: PASS, 0 warnings, 0 errors.
- Full automated .NET tests: PASS, `89/89`.
- Focused authorization/customer/SQL tests: PASS, `43/43`.
- Customers Playwright: PASS, `7/7` for `tests/playwright/specs/customers-001.spec.ts`.

## Publish

- Canonical Publish path: `D:\Working\T.A\Tech\Projects\TAPortal\sourceN\TAPortal\publish-TAPortal-blazor`
- Release publish completed from `src/TAPortal.Web/TAPortal.Web.csproj` using Release configuration.
- Backup created before replacement:
  `D:\Working\T.A\Tech\Projects\TAPortal\sourceN\TAPortal\publish-backup-ADMIN-RBAC-006-20261012-170110`
- No ZIP was created.
- No IIS deployment, restart, or production copy was performed.

## Artifact verification

- Release DLL SHA-256:
  `8F990C13B2BC36BB508A4CC2062D42D915F93285E4940DB37FEE4072C7F31FAA`
- Publish DLL SHA-256:
  `8F990C13B2BC36BB508A4CC2062D42D915F93285E4940DB37FEE4072C7F31FAA`
- Release and Publish DLL hashes match.
- `web.config`: present.
- CSS: `wwwroot/css/ta-portal.css` present.
- Customer JS: `wwwroot/js/taportal-customers.js` present.
- Runtime dependencies: `TAPortal.Web.deps.json`, `TAPortal.Web.runtimeconfig.json`, executable, and referenced .NET/SQL assemblies present.
- Publish ZIP count: `0`.

## appsettings preservation

The pre-publish canonical `appsettings.json` was backed up and restored after publishing. The resulting file was compared byte-for-byte with the pre-publish copy and matched.

- Resulting `appsettings.json` SHA-256:
  `4327504584B03D01F4AF2A43CB5BE5810B63878A443E5C79876AE9F499C0A537`
- No credentials were added to Git.

## Git status

No reset, stash, `git add .`, `git add -A`, force push, commit, or push was performed. The repository contains pre-existing overlapping changes, including staged `src/TAPortal.Web/appsettings.json` deletion; no unrelated files were staged or merged. The generated temporary publish directory was removed. The timestamped publish backup remains outside the canonical publish contents for rollback.

## Direct-folder copy readiness

The canonical Publish folder contains the fixed Release output and is ready for an owner-controlled direct folder copy to IIS. Production acceptance remains pending.