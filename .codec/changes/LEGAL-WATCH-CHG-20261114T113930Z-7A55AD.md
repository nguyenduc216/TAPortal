# Change LEGAL-WATCH-CHG-20261114T113930Z-7A55AD

- Feature ID: `LEGAL-WATCH`
- Change ID: `LEGAL-WATCH-CHG-20261114T113930Z-7A55AD`
- Created UTC: 2026-11-14T11:39:30.748593+00:00
- Tiêu đề: Monthly tax-law watch: Cong bao + vbpl queue for the law library
- Trạng thái: IMPLEMENTED (deploy recorded in the report)

## Yêu cầu và phạm vi
Operator 2026-11-14 (relayed by the main session, verbatim): "tôi chấp nhận luôn, bạn báo phiên nó chạy cho chúng tôi cái" —
proposal option A: TAPortal tự phát hiện văn bản pháp luật thuế mới/sửa đổi và đưa vào Kho văn bản pháp luật của
"Hợp đồng dịch vụ" qua hàng chờ duyệt, không tự nạp thẳng.
- Weekly: RSS "Các văn bản mới ban hành" of congbao.chinhphu.vn (about 15 days / 50 entries) → the document page of entries
  whose summary names a tax subject (or whose number is -BTC) → NEW queue entry when the summary has a tax keyword; no
  duplicate per number; nothing already in the library.
- Monthly: same read, plus each library document's vbpl.vn page (located once through the vbpl.vn sitemap by number + type,
  then remembered in LegalDocuments.SourceUrl) → STATUS queue entry when the effect status or expiry differs; first time
  with a normal status only records a baseline. Then an email (system SMTP channel) to active staff with LEGAL.LIBRARY.MANAGE.
- Queue page Hợp đồng dịch vụ → Kho văn bản pháp luật → "Văn bản mới cần duyệt" (/LegalWatch): view with CONTRACTS.REVIEW.VIEW;
  "Nạp vào kho" (Word file from Công báo, saved through SqlLegalLibraryStore.SaveAsync: same validation, audit, article split),
  "Nhập tay" (library form pre-filled, nothing saved until the person saves), "Cập nhật hiệu lực" (STATUS), "Bỏ qua" (note),
  "Kiểm tra ngay" (background run, at most once per 10 minutes). All decisions need LEGAL.LIBRARY.MANAGE (checked in SQL), audited.
- Out of scope / decided: no new permission (LEGAL.LIBRARY.MANAGE decides the queue, same as entering a law by hand);
  no công văn (gdt.gov.vn: no feed; option B not chosen); thuvienphapluat.vn (commercial, 403 to automated access) and
  mof.gov.vn (script-only) not read; vbpl.vn /api/ (disallowed by robots.txt) not used, so relations are read from the
  summary text; keywords are in code (LegalWatchPolicy.Keywords), not editable on a page; Claude summary of changes not built
  (optional in the proposal).

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `database/migrations/048-legal-watch.sql`
- ADD `database/scripts/085-verify-legal-watch.sql`
- ADD `database/scripts/086-rollback-legal-watch.sql`
- ADD `src/TAPortal.Web/Services/LegalWatchSources.cs` (policy, keyword filter, relation reader, Công báo and vbpl.vn readers)
- ADD `src/TAPortal.Web/Services/LegalWatchStore.cs` (ILegalWatchStore, SqlLegalWatchStore)
- ADD `src/TAPortal.Web/Services/LegalWatchService.cs` (HTTP with host allow-list, run logic, monthly email, trigger, scheduler)
- ADD `src/TAPortal.Web/Controllers/LegalWatchController.cs`
- ADD `src/TAPortal.Web/Views/LegalWatch/Index.cshtml`
- MODIFY `src/TAPortal.Web/Controllers/LegalLibraryController.cs` (Create GET accepts a pre-filled Input from the query string)
- MODIFY `src/TAPortal.Web/Views/LegalLibrary/Index.cshtml` (button "Văn bản mới cần duyệt")
- MODIFY `src/TAPortal.Web/Views/Shared/_Layout.cshtml` (sidebar "Hợp đồng dịch vụ" also current on /LegalWatch)
- MODIFY `src/TAPortal.Web/Models/ContractReviewModels.cs` (LegalWatchIndexVm)
- MODIFY `src/TAPortal.Web/Program.cs` (store, service, trigger, HTTP client without auto-redirect, hosted scheduler)
- ADD `tests/TACustomerPortal.Tests/SafeLegalWatchTests.cs` (11 tests)

## Migration SQL
- Database: TAPortalSys (WIN-HSHL54Q184K\MSSQLSERVER2014)
- Order: after 044 and 046 (047 was a one-off data seed of the CONTRACT-REVIEW session, never committed). Number reserved with that session.
- `database/migrations/048-legal-watch.sql`: dbo.LegalWatchRuns, dbo.LegalWatchItems (filtered unique indexes per number / per
  document+status), dbo.LegalDocuments + SourceUrl, EffectStatus, StatusCheckedAt (nullable). Schema only, no row changed.
- Verify `database/scripts/085-verify-legal-watch.sql` (VERIFY_STATUS=PASS / NOT_APPLIED). Rollback
  `database/scripts/086-rollback-legal-watch.sql` (drops both tables and the three columns; THROW 51002 while a decided entry exists).
- Execution (Invoke-TAMigrations.ps1, plan 048:085:086, 2026-11-14 12:00–12:01Z): pre-check NOT_APPLIED; dry-run in a rolled-back
  transaction afterMigration=PASS afterRollback=NOT_APPLIED trancount0=True; pre-check again NOT_APPLIED; latest full backup 298 min
  old; gates PASS (771 Safe tests); real run exit 0 VERIFY_STATUS=PASS → **EXECUTED**.

## Test và kết quả
- `dotnet restore TAPortal.sln`; `dotnet build TAPortal.sln -c Release --no-restore`: 0 errors.
- Safe tests: 771 passed, 0 failed (11 new in SafeLegalWatchTests: migration rules, feed/page/vbpl/sitemap readers on fragments
  of the real pages, host allow-list, job dedup/baseline, monthly email text, page permissions, import through the library store,
  unreadable Word file never reaching the library).
- Readers also run once (throwaway test, not committed) on pages fetched 2026-11-14: Công báo RSS 50 entries / 8 opened; document
  page 166/2026/TT-BTC read with type, agency, dates and Word link; vbpl.vn 17/2025/NĐ-CP read (Hết hiệu lực một phần); sitemap
  matched 91/2015/QH13, 108/2025/QH15, 123/2020/NĐ-CP, 80/2021/TT-BTC, 29/2023/QH15; 43/2024/QH15 is not in the vbpl.vn sitemap.
- Reachability from the production server (read-only, 2026-11-14): congbao RSS 200, vbpl.vn sitemap 200, Công báo Word file 200
  (DOCX), signed PDF 200.

## Publish / Git
Recorded in `.codec/reports/LEGAL-WATCH-CHG-20261114T113930Z-7A55AD.md`.

## Rủi ro, quyết định và việc còn lại
- The readers depend on the HTML of congbao.chinhphu.vn and the embedded data of vbpl.vn; a site redesign makes a run read
  nothing (run FAILED or 0 entries, visible on the queue page) — no wrong data enters the library because every entry is reviewed.
- RSS covers about 15 days: the weekly run is what keeps the coverage; if the portal is down more than ~2 weeks, documents of
  that gap are missed (not back-filled).
- Library documents not in the vbpl.vn sitemap (e.g. 43/2024/QH15) are not status-checked; the sitemap (≈40 MB) is read again
  each month only while such documents exist.
- Monthly email only goes out if the system SMTP channel (Cài đặt thông báo) is enabled; otherwise the queue page still shows everything.
- Follow-ups not done: công văn of Cục Thuế (option B), editable keyword list, Claude summary of changes.
