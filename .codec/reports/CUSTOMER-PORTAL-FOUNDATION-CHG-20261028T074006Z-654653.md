# DEV/TEST policy and Customer Portal Checkpoint 3 local publish

FEATURE_ID: CUSTOMER-PORTAL-FOUNDATION
CHANGE_ID: CUSTOMER-PORTAL-FOUNDATION-CHG-20261028T074006Z-654653
Branch: feature/customer-portal-mvp01
Git source SHA: ff965efc17b2e0aefde8b603df7269a8d29fbcfb
Publish: PUBLISHED (local artifact only), exit code 0.
Target: D:\T.A\TAPortal\TAPortal\publish\deployed-TACustomerPortal.Web
Artifact SHA256: 40442b0023252bc9032f75036a477dfff0dff8e458c735cbd64a738e02bdcb66
Published TACustomerPortal.Web.dll SHA256: cb6dd913c3f9bc4e6454208bf57c67749279dbf7b3d3b2290290fcb6b62d521f
Database: VERIFIED_WITH_WARNINGS (operator supplied PASS_WITH_WARNINGS for migration 008; no new database verification).
IIS: NOT_CHECKED / NOT_DEPLOYED. Merge SHA: none; main not merged.

## Objective, scope and decisions
Apply the explicitly requested DEV/TEST policy and resume Customer-only local artifact publish. No production source edits. Supersedes the previous IIS-preflight blocker; previous reports/history remain historical and unchanged. Policy/config/validator prohibit IIS/server probing, Administrator requests, SQL writes, database creation and mutating integration tests. Read queries require read-only permissions; unavailable access is NOT_VERIFIED. SQL verification defaults to PRINT TEXT; migration files are operator-executed only. No SQL run and migration 008 not rerun.

## Validation and artifact evidence
- Correct branch, source HEAD recorded above. Existing user edit database/scripts/004-verify-customer-portal.sql preserved and excluded from commit. Artifact is from tracked application source; only policy and metadata changed.
- dotnet build TAPortal.sln -c Release: PASS, 0 warnings / 0 errors.
- dotnet test tests/TACustomerPortal.Tests/TACustomerPortal.Tests.csproj -c Release --no-build --filter FullyQualifiedName~SafePortalTests --logger trx --results-directory TestResults: PASS, 34 passed / 0 failed / 0 skipped. Memory-only store and invalid SQL configuration; legacy SQL tests not selected.
- TRX: TestResults/PC_DESKTOP-LF41200_2026-10-28_14_40_49_net8.0.trx (ignored).
- python -m unittest discover -s .codec/tools -p test_codec.py: PASS, 3 tests, including expanded policy-regression cases.
- python .codec/tools/codec.py validate: PASS. git diff --check: PASS.
- Local preflight: target initially absent; publish parent exists; all ancestor paths have no reparse points. Created only the conventional target and verified write permission with a temporary probe inside it, then removed the probe. No existing artifact replaced and no cleanup performed.
- dotnet publish src/TACustomerPortal.Web/TACustomerPortal.Web.csproj -c Release --no-build --no-restore -o <target>: PASS, exit 0.
- Verified nonempty DLL, deps.json, runtimeconfig.json, web.config and populated wwwroot; parsed JSON/XML. Published portal/shared DLL bytes match Release build outputs.
- Complete artifact: 54 files. Manifest `.codec/reports/CUSTOMER-PORTAL-FOUNDATION-CHG-20261028T074006Z-654653.sha256` contains sorted relative file paths and each file SHA256, encoded UTF-8 with LF. Artifact SHA256 above hashes the exact manifest bytes; the manifest is outside the artifact directory. DLL hash is reported separately.
- No IIS access/configuration, server probing, SQL, app startup, integration tests or deployment. TAPortal.Web not published.

## Changed files
- AGENTS.md
- .agents/skills/codec/SKILL.md
- .codec/README.md
- .codec/config.json
- .codec/tools/codec.py
- .codec/tools/test_codec.py
- docs/customer-portal-runbook.md
- .codec/features/CUSTOMER-PORTAL-FOUNDATION/README.md
- .codec/features/CUSTOMER-PORTAL-FOUNDATION/history.json
- .codec/changes/CUSTOMER-PORTAL-FOUNDATION-CHG-20261028T074006Z-654653.md
- .codec/reports/CUSTOMER-PORTAL-FOUNDATION-CHG-20261028T074006Z-654653.md
- .codec/reports/CUSTOMER-PORTAL-FOUNDATION-CHG-20261028T074006Z-654653.sha256

## Rollback and remaining work
Revert this change commit after review to roll back policy/report source; preserve unrelated user work. Local artifact removal/replacement is a separate explicit action; no automatic cleanup or SQL rollback. Operator membership provisioning and functional acceptance remain pending. Any future live deployment requires separate approval and operator management outside this DEV/TEST task.
Delivery commit SHA and push result are reported in the final response to avoid a self-referential commit hash. Source SHA above identifies application source built and published.
