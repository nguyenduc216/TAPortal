# TAPORTAL-20261011-BLAZOR-MIGRATION-007 — Integration hardening

- Date: 2026-10-11
- Agent: Codex
- Project: TAPortal
- Branch: `feat/compact-dynamic-navigation`
- Starting SHA: `4a644bcd92d7b7ede82860902404d3498facc923`
- FEATURE_ID: `TAPORTAL-PLATFORM-BLAZOR-MIGRATION`
- CHANGE_ID: `TAPORTAL-20261011-BLAZOR-MIGRATION-007`

## Request and scope

Audit and harden the integrated Phase 1–6 Blazor trial implementation without cutting over MVC routes or adding business features. Establish real component rendering tests, audit server write authorization, prepare repeatable browser/staging infrastructure, classify the entire dirty worktree, perform a secret-safe checkpoint, and report cutover readiness honestly.

## Audit result

All nine trial routes exist and render through Interactive Server under authenticated routing. MVC production paths remain separate. The route audit identified incomplete browser evidence, silent Roles/Menus load errors, missing Department/Team edit parity, missing Team parent selection, and a minimal Dashboard error state. These are recorded rather than disguised as PASS.

The write audit found that FunctionAction-protected Users/Department/Team operations re-resolve permissions from SQL at execution time. Profile Save did not re-read circuit identity at mutation time, and password UPDATE did not include the active-account predicate. Both were minimally hardened. No authentication scheme, business owner or SQL schema was redesigned.

## Test infrastructure

- Added bUnit to the existing test project.
- Actual component tests cover one reconnect host, recursive menu output/active navigation, delayed GlobalLoading overlay, overlapping scopes and cleanup.
- Security contracts cover current identity equality, permission revalidation and active-account password mutation.
- Added a credential-free Playwright TypeScript suite plus locked dependency manifest. Anonymous and authenticated route, refresh, console/network, MVC/Blazor coexistence, multi-tab and permission-surface cases are defined. Authenticated cases skip until an external authorized storage state exists.
- Added safe staging database/account/reset guidance. No production account, data, IIS pool, migration or security setting was changed.

## Git and security

The inventory classifies source, tests, histories, audits, local appsettings, publish/browser artifacts and unrelated schema audits. The local appsettings credential is explicitly excluded. Existing critical/high follow-ups—credential rotation, TLS 1.0, public SQL access, missing OperationalDb schema ownership and publish-artifact exposure—are recorded with owners and verification criteria.

No standalone CHANGE 003 history artifact existed, although Phase 3 source comments are present. This checkpoint does not fabricate retrospective evidence; it records the governance gap while preserving the tagged implementation.

## Verification

- Tests before Phase 7 source changes: baseline expected 54/54 from Phase 6 history; the initial local invocation did not complete within its tool window and is not substituted as fresh PASS evidence.
- Release build after changes: PASS, 0 warnings/errors.
- Full tests after changes: PASS, 62/62, including 4 bUnit component tests, loading concurrency/exception cleanup and 3 security hardening tests.
- Anonymous Chromium Playwright: PASS, 1/1, including all protected route redirects and removal of the bootstrap-account hint.
- Authenticated Playwright: 3 scenarios SKIPPED/BLOCKED because no external authorized storage state exists. Write-data, reconnect/network interruption and IIS recycle remain blocked pending authorized staging resources.
- Non-production publish: PASS to `publish-TAPortal-phase7`; generated `appsettings.json` was deliberately removed so the artifact requires secret-store/environment configuration and does not duplicate the local credential.
- Publish, final diff/secret scan, commit and push are recorded in the delivery report.

## Status

`PARTIAL — Integration infrastructure ready, authenticated parity pending.`

Ready for route cutover: **NO**. Next CHANGE_ID: `TAPORTAL-20261011-BLAZOR-MIGRATION-008`.
