# CODEC DELIVERY REPORT — LEGAL-WATCH-CHG-20261114T113930Z-7A55AD

Feature ID: `LEGAL-WATCH`
Change ID: `LEGAL-WATCH-CHG-20261114T113930Z-7A55AD`

## A. Identification
- Branch `feature/legal-watch` (worktree `sourceN/TAPortal-legalwatch-wt`), created from origin/main dedfc4e; origin/main 42fae98 merged in (5871b46) before deploy.
- Operator approval (relayed verbatim by the main session, 2026-11-14): "tôi chấp nhận luôn, bạn báo phiên nó chạy cho chúng tôi cái" — option A of the proposal.

## B. Audit Summary
| ID | Project | Module | Severity | Problem | Root Cause | Fix | Files | Test | Status |
|---|---|---|---|---|---|---|---|---|---|
| LW-1 | TAPortal.Web | Law library | — (feature) | New/amended tax documents had to be found and entered by hand | No watch of official sources | Weekly Công báo + monthly vbpl.vn watch, review queue, monthly email | see change | SafeLegalWatchTests (11) | VERIFIED |
| LW-2 | TAPortal.Web | Legal watch | LOW | Fetching third-party URLs (SSRF risk) | URLs come from feeds/pages | HTTPS + 4-host allow-list, redirects followed by hand and re-checked, size limits, DTD prohibited | LegalWatchService.cs, LegalWatchSources.cs | OnlyHttpsOnTheFourSourceHostsIsAllowed, feed DTD test | VERIFIED |

## C. Customer Portal Account Management
NOT_APPLICABLE (Admin portal only).

## D. Admin CRUD
/LegalWatch queue: view with CONTRACTS.REVIEW.VIEW; import / apply status / dismiss / manual check with LEGAL.LIBRARY.MANAGE (policy + SQL check), audited (LEGAL_WATCH_IMPORTED, LEGAL_WATCH_DISMISSED, LEGAL_DOCUMENT_STATUS_APPLIED; import itself goes through SqlLegalLibraryStore.SaveAsync → LEGAL_DOCUMENT_CREATED).

## E. Changed Files
Listed in `.codec/changes/LEGAL-WATCH-CHG-20261114T113930Z-7A55AD.md`.

## F. Database
- `database/migrations/048-legal-watch.sql` — verify `database/scripts/085-verify-legal-watch.sql`, rollback `database/scripts/086-rollback-legal-watch.sql`.
- Executed 2026-11-14 12:01:49Z via `deploy/windows/Invoke-TAMigrations.ps1 -Plan 048:085:086`: pre-check NOT_APPLIED, dry-run PASS/NOT_APPLIED/trancount 0, gates PASS, real run VERIFY_STATUS=PASS → **EXECUTED**.

## G. Build & Tests
- Release build 0 errors; Safe tests 771/771 passed (also re-run by the deploy audit: 771/771); `test_codec.py` OK; `git diff --check` clean.
- `codec.py validate`: only "Publish path violates fixed convention" (paths belong to the operator workspace, not this worktree; the deploy audit treats it as a warning).

## H. Admin Publish
- `portal` (TAPortal.Web) via `deploy/windows/Deploy-TARemote.ps1 -Target portal -MigrationsApplied`: source commit 5871b46, 89 files,
  artifact manifest SHA256 `1ce2619c3bea7c8a224cb1f9e2085db7f7738d7dd96d81c666dba47eb64bb5f0`, package SHA256
  `EB7BA6D55EC008FC1906541E3E587539107F0F3ACF268908F1E9E3EB7B743CC0`, server backup `C:\TADeploy\backups\portal-20261114-190716`,
  health HTTP 200, 2026-11-14 12:07:20Z → **DEPLOYED**. Previous portal commit 0613b03 (contained in this release).

## I. Customer Publish
NOT_APPLICABLE (TACustomerPortal.Web unchanged; no Shared/Integrations change).

## J. Security and Acceptance
- Nothing enters the library without a person; the job writes only the queue and the library's SourceUrl / StatusCheckedAt and a first baseline EffectStatus.
- Outbound requests: congbao.chinhphu.vn, congbaocdn.chinhphu.vn, g7.cdnchinhphu.vn, vbpl.vn only (robots.txt of each allows the paths used; vbpl.vn /api/ not used); User-Agent `TAPortal-LegalWatch/1.0`; 0.7 s between requests.
- No secrets or PII stored; signer names on Công báo pages are not read.

## K. Git Delivery
- Source commit aeb8a91 (feature), merge commit 5871b46 (origin/main into feature), pushed to origin/feature/legal-watch.
- Merge to main: recorded in the hand-off message (a report never states its own commit SHA).

## L. Operator Handoff
- Page: Hợp đồng dịch vụ → Kho văn bản pháp luật → "Văn bản mới cần duyệt". First monthly run starts about 2 minutes after the app starts.
- The monthly email needs the system SMTP channel enabled in "Cài đặt thông báo".
- Not built: công văn of Cục Thuế (option B), editable keyword list, Claude summary of changes. Library documents not in the vbpl.vn sitemap (e.g. 43/2024/QH15) are not status-checked.
