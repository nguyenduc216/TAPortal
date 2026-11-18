# Change TINGEE-INTEGRATION-CHG-20261118T091627Z-2B4048

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261118T091627Z-2B4048`
- Created UTC: 2026-11-18T09:16:27.995203+00:00
- Tiêu đề: Reconciliation opens on Tất cả; Tất cả filter kept; selected card highlighted; shown/total line
- Trạng thái: PLANNED

## Yêu cầu và phạm vi

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path:
- Thứ tự chạy:
- Preflight / Verify / Rollback:
- Execution: NOT_EXECUTED

## Test và kết quả

## Publish
- Project:
- Destination (chỉ từ config):
- Trạng thái: NOT_PUBLISHED
- Artifact SHA256:
- Smoke test:

## Git
- Branch:
- Commit SHA:
- PR:
- Merge: PENDING_APPROVAL
- Merge SHA:

## Rủi ro, quyết định và việc còn lại

## Notes (operator 2026-11-18)
- Report: "Tiền vào 10 but the list shows 8". Not missing data: prod has 8 UNMATCHED + 2 MATCHED (21.200.000 ₫ MB Bank, order DH261110-0001); the list defaulted to "Cần xử lý" and the "Đã khớp đơn" card was off-screen.
- Bug: choosing "Tất cả" (value "") and Lọc came back as "Cần xử lý": an empty status binds as missing and fell back to the default; the card link with status "" dropped the parameter too. Now "Tất cả" is an explicit code `ALL` (PaymentTxText.All) in both apps; the store treats it as no status filter.
- Admin Đối soát giao dịch opens on "Tất cả"; the selected card is highlighted (blue); six cards share the row on wide screens (ta-kpi-grid--6); a line "Đang hiện x / y giao dịch · trạng thái … · Xem tất cả".
- Tests: SafeCustomerWorkspaceTests.TAReconcilesEveryCustomersTransactionsIncludingUnattributedOnes.
- portal: commit 2ed8885, artifact 6804d3394c2685482ff2ceb2746e9697ff7e8d1e4a3e8574bdce2914bad9d20b, DEPLOYED 2026-11-18T09:23:21Z, health 200.
- invoice: commit 2ed8885, artifact 5d499f6c1da92ad54adc11783abdde8179f9e05b34771a9b7a9b54bd78f247f6, DEPLOYED 2026-11-18T09:28:04Z, health 200 (-MigrationsApplied: 066 by WEBSITE-TA, checked on prod).
