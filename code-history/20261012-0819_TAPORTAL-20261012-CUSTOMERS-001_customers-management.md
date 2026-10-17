# TA Portal Customers Management

- Date: 2026-10-12
- Agent: OpenAI Codex
- FEATURE_ID: `TAPORTAL-CUSTOMERS-MANAGEMENT`
- CHANGE_ID: `TAPORTAL-20261012-CUSTOMERS-001`
- Branch: `feat/compact-dynamic-navigation`
- Commit SHA: see Git commit containing this history entry

## Scope

- Completed the customer create flow for MVC `/customers` and Blazor `/blazor/customers`.
- Added server-side permission checks, validation, duplicate-code handling, anti-forgery protection for MVC, loading/error states and duplicate-submit protection.
- Added filtered XLSX export with server-side authorization, date-based filename, safe inline text cells and no internal audit fields.
- Refined shared TA action buttons, customer table, responsive overflow behavior and compact empty state.

## Files changed

The implementation files are present in the working tree under `src/TAPortal.Web`, with focused tests under `tests/TAPortal.Web.Tests`, the isolated customer harness under `tests/TAPortal.CustomerHarness`, and the Playwright scenario under `tests/playwright/specs/customers-001.spec.ts`. This history entry is the only new history file for this change.

## Database audit

- Read-only source/schema audit confirms `dbo.Customers` has a GUID primary key, unique filtered active-row `Code` index, nullable `TaxCode`, contact fields, `Status`, active/soft-delete flags, audit timestamps/users and rowversion.
- TaxCode is indexed but not unique in the audited schema.
- No Customers schema migration was required or executed.
- The action metadata SQL remains review-only and was not run against production.

## Implementation summary

- `PortalDb` uses parameterized SQL and rechecks `Customers.Index` and `Customers.Create` before insert.
- MVC retains route ownership at `/customers`; Blazor remains at `/blazor/customers` with shared service/business rules.
- Export is read-only and uses the current search filter.
- No production INSERT/UPDATE/DELETE/DDL was used by tests.

## Build/tests

- `dotnet restore TAPortal.sln`: PASS.
- `dotnet build TAPortal.sln -c Release --no-restore`: PASS, 0 warnings/errors.
- `dotnet test TAPortal.sln -c Release --no-build`: PASS, 86/86.
- Focused customer tests: PASS, 7/7.
- `git diff --check`: PASS; Git emitted only existing line-ending normalization warnings.

## Playwright

- Isolated `@customers-001` suite: PASS, 7/7.
- Covered MVC and Blazor at 1440, 768 and 390 widths, modal, validation, duplicate code, search, XLSX download, permission revocation, anti-forgery, console errors and horizontal overflow.

## Publish

- Publish directory: `D:\Working\T.A\Tech\Projects\TAPortal\sourceN\TAPortal\publish-TAPortal-blazor`
- Existing output backup: `D:\Working\T.A\Tech\Projects\TAPortal\sourceN\publish-TAPortal-blazor-backup-20261012-081820`
- Release publish: PASS.
- Publish output: 58 files; DLL, static assets, CSS/JS and `web.config` verified.
- Existing `appsettings.json` was preserved byte-for-byte.
- Deploy ZIP: `D:\Working\T.A\Tech\Projects\TAPortal\sourceN\TAPortal-Blazor-IIS-Deploy.zip`
- ZIP extraction: PASS, 58/58 files.
- ZIP SHA-256: `97A7AE7354A471936A846BAFEE44AE43CFC85CFE2C2AB7C7B8BFAE4A17CF2437`

## Git status

- Working tree contains pre-existing and concurrent changes outside this history entry, including staged `appsettings.json` deletion and unrelated migration/UI/history artifacts.
- No commit or push was performed to avoid staging unrelated changes or credentials/configuration.

## Remaining blockers

- Production permission assignment for `CRM.CUSTOMERS.CREATE` requires owner approval; the review SQL intentionally grants no role/user access.
- IIS deployment and production replacement were not performed.
- ZIP and publish binaries are local deployment artifacts and must not be committed.