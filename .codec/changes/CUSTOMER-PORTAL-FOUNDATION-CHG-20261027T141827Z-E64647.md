# Customer Portal MVP 01 - Checkpoint 1

FEATURE_ID: CUSTOMER-PORTAL-FOUNDATION
CHANGE_ID: CUSTOMER-PORTAL-FOUNDATION-CHG-20261027T141827Z-E64647
Branch: feature/customer-portal-mvp01
PR: https://github.com/nguyenduc216/TAPortal/pull/7 (open, draft)
Status: SOURCE_AND_SAFE_TESTS_COMPLETE; operator DB/IIS acceptance pending.

## Intake and verified baseline
AGENTS.md, Codec skill/config and related Codec installation history read first. No prior CUSTOMER-PORTAL-FOUNDATION history existed, so initialized this feature/change. Source and PR review establish the existing baseline: separate Admin/Customer apps; shared PasswordService and MembershipAdminStore; login/logout, membership provisioning/reactivation/locking, active SYS_ADMIN recheck in store, multiple-customer selection, dashboard, read-only company/personal profiles and shared password changes already implemented. Preserved these functions; no rewrite. PR previously accepted SYS_ADMIN-only scope; Agent delegation remains out of scope.

## Changes and technical decisions
- Preserve original authenticated ticket properties when switching companies; previously each switch reset the fixed two-hour expiry. Regression test checks actual protected cookie IssuedUtc/ExpiresUtc across switch.
- Add no-store response caching on customer account/dashboard/profile controllers to avoid caching personal/tenant responses.
- Add TAPortalSys guard before transactions in existing migration 008 and rollback 005; no duplicate migration created.
- Legacy SQL integration tests create/drop databases and execute seed/rollback, violating Codec. Preserve their code, mark skipped and block IAsyncLifetime initialization/cleanup. New SafePortalTests substitute an in-memory store, invalid SQL config and ephemeral data protection. Tests do not connect to SQL or launch an external web server.
- CI no longer starts LocalDB or runs dotnet publish (including MCP publish). Push therefore runs build/safe tests only. Ignore local TestResults; no secrets/logs committed.
- Admin production source and TAPortal.Shared unchanged. Existing store List/Save/SetActive require active SYS_ADMIN; mutations and permission checks share a serializable transaction. Existing CustomerStore parameterized membership query requires active user/customer/membership; customer context comes from validated claims, never request CustomerId. Existing shared-password stamp revokes both apps' sessions on subsequent validation.

## Acceptance coverage and limits
Safe HTTP coverage: single/multiple membership login, failed/inactive/no-membership login, logout POST-only, anonymous route denial, company switching/foreign-ID rejection, two-user tenant/profile isolation, user/membership/password revocation, valid/invalid/concurrent password changes, CSRF on all mutating customer endpoints, secure cookies, no-store, normalized account and forged-forwarded-IP throttle. Admin coverage resolves actual SystemAdminOnly policy for SYS_ADMIN versus Agent/portal roles/direct permission, verifies controller policy attributes and rejects renamed Customer cookie in Admin before SQL.
SQL provisioning/locking/reactivation, constraints/transactions and live Admin role/password revocation are SOURCE-REVIEWED only in this checkpoint, NOT live DB integration-tested. No claim of complete live Admin regression or browser/IIS UAT. Store-level SQL tests remain skipped; in-memory tests cannot establish real schema/SQL behavior. No actual schema audit was run.

## Build/test evidence
- dotnet build TAPortal.sln -c Release --no-restore: PASS, 0 warnings, 0 errors.
- dotnet test TAPortal.sln -c Release --no-build --logger trx --results-directory TestResults: PASS; 34 passed, 0 failed, 25 skipped legacy test methods (theories skipped before expansion; prior 42 SQL cases are historical).
- Initial safe-test runs exposed test-harness issues (scoped authorization service, duplicate Set-Cookie on sign-out/sign-in, GET logout returns 405); corrected expectations/scope/cookie selection; final run green.
- Local TRX: TestResults/PC_DESKTOP-LF41200_2026-10-27_21_22_16_net8.0.trx (ignored).
- python .codec/tools/codec.py validate: POLICY PASS; publish configuration pending.
- git diff --check: PASS.

## Files changed
- .codec/changes/CUSTOMER-PORTAL-FOUNDATION-CHG-20261027T141827Z-E64647.md
- .codec/features/CUSTOMER-PORTAL-FOUNDATION/README.md
- .codec/features/CUSTOMER-PORTAL-FOUNDATION/history.json
- .codec/reports/CUSTOMER-PORTAL-FOUNDATION-CHG-20261027T141827Z-E64647.md
- .github/workflows/build.yml
- .github/workflows/customer-portal.yml
- .gitignore
- database/migrations/008-customer-portal-memberships.sql
- database/scripts/005-rollback-customer-portal.sql
- docs/customer-portal-runbook.md
- src/TACustomerPortal.Web/Controllers/AccountController.cs
- src/TACustomerPortal.Web/Controllers/DashboardController.cs
- src/TACustomerPortal.Web/Controllers/ProfileController.cs
- tests/TACustomerPortal.Tests/PortalTests.cs
- tests/TACustomerPortal.Tests/SafePortalTests.cs

## SQL handoff - NOT_EXECUTED
Only target: WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys. Operator uses DBeaver or SSMS; Codex must not execute SQL mutations.
1. Backup and run read-only audit: D:/T.A/TAPortal/TAPortal/database/scripts/003-customer-portal-schema-audit.sql. Confirm Users/Customers/Permissions/Roles/UserRoles schema, status/auth columns, current membership mapping and compatible constraints. If equivalent or incompatible relationships exist, stop and review mapping.
2. Only if audit confirms needed and approved: D:/T.A/TAPortal/TAPortal/database/migrations/008-customer-portal-memberships.sql. Existing script updated, not duplicated. Creates CustomerPortalMemberships and compatibility permissions. Guard checks correct DB before transaction. Do not rerun legacy migrations 001-007 (USE TAPortal).
3. Read-only verify: D:/T.A/TAPortal/TAPortal/database/scripts/004-verify-customer-portal.sql. Confirm FKs, unique pair, role constraint, no duplicate/invalid roles. Operator performs functional membership acceptance afterward.
4. Review commented grants guidance: D:/T.A/TAPortal/TAPortal/database/scripts/006-customer-portal-least-privilege.sql with actual identities.
Rollback is not part of installation: D:/T.A/TAPortal/TAPortal/database/scripts/005-rollback-customer-portal.sql, operator approval and backup required, refuses populated table and wrong DB. No SQL was run, including seeds/rollback. No database created.

## Delivery and remaining work
Implementation commit SHA: 1d048d1ec3b52cda301c18154949bb1e1ac41c8f. Follow-up metadata records this SHA; final delivery HEAD reported separately.
Implementation push: PUSHED. PR #7 description updated; remains OPEN/DRAFT. Merge: PENDING_APPROVAL, merge SHA: none.
Publish: NOT_PUBLISHED. Deploy: NOT_DEPLOYED. Artifact SHA256: N/A.
Both publish targets remain PENDING_CONFIGURATION; this did not block source/test work.
Operator pending: review/audit actual TAPortalSys schema and existing migration state, manually apply only required SQL, validate real provisioning/revocation/tenant isolation and Admin regression; later approve exact fixed publish paths and IIS HTTPS/key persistence/ACL/proxy/WAF/browser UAT. No merge or publish authorization inferred.
Rollback source if required: revert only checkpoint commits after review; retain history. Database rollback is a separate operator decision.
