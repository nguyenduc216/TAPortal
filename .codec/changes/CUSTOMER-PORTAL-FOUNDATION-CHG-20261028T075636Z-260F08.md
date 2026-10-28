# Customer Portal MVP01 - Checkpoint 4: Local smoke and functional acceptance

FEATURE_ID: CUSTOMER-PORTAL-FOUNDATION
CHANGE_ID: CUSTOMER-PORTAL-FOUNDATION-CHG-20261028T075636Z-260F08
Branch: feature/customer-portal-mvp01
Verified starting Git HEAD: 531409707fc26f0d216e11270d601f2a91ec49dc
Application artifact source SHA (Checkpoint 3): ff965efc17b2e0aefde8b603df7269a8d29fbcfb
Application source unchanged since artifact publish. Delivery metadata commit SHA/push result reported separately to avoid self-reference.
CHECKPOINT_4A: PASS (artifact startup and safe anonymous HTTP scope).
CHECKPOINT_4B: PENDING_OPERATOR.
CUSTOMER_PORTAL_MVP01: NOT_YET_ACCEPTED.
IIS: NOT_CHECKED / NOT_DEPLOYED. Merge: NOT_MERGED; merge SHA: none.
Database access: NOT_VERIFIED / NOT_ACCESSED this checkpoint. Earlier migration 008 PASS_WITH_WARNINGS is operator-supplied historical evidence only, not a new database verification.

## Objective, scope, dependencies and safety
Use the existing artifact without modification or republish; establish safe local startup/HTTP behavior and prepare manual UAT. Read Codec contract/config, feature README/history, related checkpoint evidence and runbook first. Preserved pre-existing database/scripts/004-verify-customer-portal.sql edit; not executed or staged. No IIS/server inspection, service queries, elevation, SQL, migration, seed, rollback, real login, business mutation or integration tests against a live DB.
Source review: Program.cs, CustomerStore.cs, CustomerSecurity.cs, account/dashboard/profile controllers, layout/login views, Admin membership controller/view, shared password policy and Admin SYS_ADMIN authorization. CustomerStore opens SQL lazily inside business methods; startup and anonymous GET do not invoke these methods. Successful login calls TouchLogin (writes LastLoginAt), so real login was deliberately left to operator. No source defect found within examined scope; no application source/test/config changes.
Runtime prerequisite: .NET 8 ASP.NET Core runtime resolved successfully by actual startup. No live SQL credentials needed for anonymous smoke. Explicit INVALID database configuration overrides any configured connection; no cookies supplied. Production environment uses non-developer error handling. Dedicated temporary Data Protection directory outside artifact is protected by Windows DPAPI and removed after process stop. Process stdout/stderr discarded; no runtime logs, secrets, tokens, cookies or response bodies persisted.

## Artifact verification
Target: D:\T.A\TAPortal\TAPortal\publish\deployed-TACustomerPortal.Web
Artifact SHA256: 40442b0023252bc9032f75036a477dfff0dff8e458c735cbd64a738e02bdcb66
Reproduced prior exact method: enumerate all artifact files using sorted Path.rglob order, create each line as lowercase file SHA256 + two spaces + relative POSIX path + LF, encode UTF-8 and hash complete manifest bytes. 54 files. Existing manifest content matches after CRLF-to-LF normalization for Git checkout portability; no historical file edited. Reproduced digest equals prior expected digest exactly.
Nonempty DLL/deps.json/runtimeconfig.json/web.config/css/customer.css/js/customer.js/images/favicon-32x32.png verified; web.config parses. All 22 runtime/native dependency entries in deps.json resolve in artifact. Startup also validates framework/dependency resolution. Artifact hash identical before and after smoke, no file added/changed; no republish. Prior manifest reference: .codec/reports/CUSTOMER-PORTAL-FOUNDATION-CHG-20261028T074006Z-654653.sha256.

## Local startup and HTTP smoke
Local URL used: http://127.0.0.1:5187. Port availability checked by loopback bind before launch; startup readiness confirmed by login GET. Launched published DLL with explicit --urls, --contentRoot, --Database:ConnectionString INVALID placeholder, --DataProtection:KeyPath temporary directory and logging disabled; child environment removes inherited ASPNETCORE_/DOTNET_/database/key/Kestrel/proxy overrides before applying Production. Working directory is artifact folder; no artifact config edited.
STARTUP: PASS. Process created solely for checkpoint, terminated and waited; process_stopped=true. Endpoint is no longer intentionally served after test; unrelated processes untouched.

| Method/path | HTTP | Outcome |
|---|---|---|
| GET /account/login | 200 | PASS; Cache-Control no-store,no-cache |
| GET / | 302 | PASS; redirect to login with ReturnUrl |
| GET /profile/company | 302 | PASS; redirect to login |
| GET /profile/personal | 302 | PASS; redirect to login |
| GET /account/select-customer | 302 | PASS; redirect to login |
| GET /account/change-password | 302 | PASS; redirect to login |
| GET /account/access-denied | 403 | PASS; no-store,no-cache |
| GET /css/customer.css | 200 | PASS; text/css |
| GET /js/customer.js | 200 | PASS; text/javascript |
| GET /images/favicon-32x32.png | 200 | PASS; image/png |

10 safe GET checks PASS, no unexpected 500. No known secret/stack-trace markers detected in returned bodies; no X-Powered-By or ASP.NET version headers. Server: Kestrel reveals family only. Bodies and Set-Cookie values not saved. Structured sanitized evidence: .codec/reports/CUSTOMER-PORTAL-FOUNDATION-CHG-20261028T075636Z-260F08-smoke.json.
Limits: no authenticated content, cache/session behavior or SQL authorization verified against real data; anonymous authorization challenges have no Cache-Control but contain no protected content. Authenticated no-store behavior remains operator UAT. HTTP-only loopback smoke does not verify HTTPS redirect/HSTS/secure cookies. External Tabler CDN stylesheet referenced by layout was not fetched; browser rendering/CDN access remain operator acceptance. No browser screenshot/visual UAT claimed. Generic /account/error is a deliberate error endpoint, not requested as a smoke success route.

## Manual acceptance
Checklist: docs/customer-portal-checkpoint4-uat.md. UAT-01 provisioning, UAT-02 login, UAT-03 dashboard/profile/cache, UAT-04 multiple companies/fixed two-hour expiry, UAT-05 tenant isolation, UAT-06 revocation, UAT-07 password/session revocation, UAT-08 Admin role isolation, UAT-09 regression/authentication boundaries: all NOT_RUN, evidence absent. Each has description, preconditions, exact operator actions, expected/actual result, status, evidence reference and notes. No manual PASS assumed from automated tests.
Operator prerequisites: approved test identities/customers, separately configured Admin/Customer process secrets and key directories, appropriate application business permissions, secure localhost environment and evidence collection. Do not supply secrets in chat/report. Functional login writes LastLoginAt; mutations and account cleanup belong to operator only. No new DB or migration is needed by this checkpoint.

## Build/test and validation
- dotnet build TAPortal.sln -c Release --no-restore: PASS, 0 warnings/errors.
- dotnet test tests/TACustomerPortal.Tests/TACustomerPortal.Tests.csproj -c Release --no-build --filter FullyQualifiedName~SafePortalTests --logger trx --results-directory TestResults: PASS, 34 passed, 0 failed, 0 skipped; legacy SQL tests not selected.
- TRX (ignored): TestResults/PC_DESKTOP-LF41200_2026-10-28_14_57_56_net8.0.trx.
- python -m unittest discover -s .codec/tools -p test_codec.py: PASS, 3 tests.
- Codec validation and git diff --check: PASS before commit.

## Changed files
- docs/customer-portal-checkpoint4-uat.md
- docs/customer-portal-runbook.md
- .codec/features/CUSTOMER-PORTAL-FOUNDATION/README.md
- .codec/features/CUSTOMER-PORTAL-FOUNDATION/history.json
- .codec/changes/CUSTOMER-PORTAL-FOUNDATION-CHG-20261028T075636Z-260F08.md
- .codec/reports/CUSTOMER-PORTAL-FOUNDATION-CHG-20261028T075636Z-260F08.md
- .codec/reports/CUSTOMER-PORTAL-FOUNDATION-CHG-20261028T075636Z-260F08-smoke.json

## Blockers, rollback and next actions
No remaining blocker for Checkpoint 4A anonymous scope. Full acceptance awaits operator-run UAT evidence, approved real-data test setup and HTTPS/browser checks; database access remains NOT_VERIFIED. Run the manual checklist and record actual outcomes; only then decide acceptance. IIS deployment and main merge stay outside scope.
Rollback documentation by reverting this checkpoint commit after review; preserve unrelated user edits. No application/artifact/SQL rollback required. Temporary process/key directory already removed. Publish status remains prior PUBLISHED; no new publish or live deployment this checkpoint.
