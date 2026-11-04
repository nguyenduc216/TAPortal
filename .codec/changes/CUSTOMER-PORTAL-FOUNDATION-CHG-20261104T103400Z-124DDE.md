# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261104T103400Z-124DDE

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261104T103400Z-124DDE`
- Created UTC: 2026-11-04T10:34:00.041145+00:00
- Tiêu đề: Xoá 10 khách hàng mẫu KH0001-KH0010 (giữ TVGPTA), có bảng lưu và rollback
- Trạng thái: IMPLEMENTED / DRY_RUN_PASS

## Yêu cầu và phạm vi
Operator (2026-11-04): "xoá hết dữ liệu khách hàng trừ TVGPTA trên portal"; chọn "Xoá hẳn, có bản lưu".
- Kiểm kê (chỉ đọc): ngoài TVGPTA có 10 khách hàng KH0001–KH0010; dữ liệu kèm theo chỉ có 10 dòng `dbo.CustomerContacts`. Không có tài khoản portal, ngân hàng, người mua, đơn hàng, cài đặt thanh toán, Tingee merchant nào thuộc các khách hàng này. `dbo.AuditLogs` giữ nguyên.
- Migration chép các khách hàng và liên hệ cần xoá sang `dbo.Backup20261104_Customers` / `dbo.Backup20261104_CustomerContacts`, rồi xoá; tự dừng nếu bảng con nào khác (theo FK vào dbo.Customers) còn dữ liệu của khách hàng sắp xoá.


## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `database/migrations/017-purge-sample-customers.sql`
- ADD `database/scripts/023-verify-purge-sample-customers.sql`
- ADD `database/scripts/024-rollback-purge-sample-customers.sql`

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path: database/migrations/017-purge-sample-customers.sql
- Thứ tự chạy: sau 016; backup COPY_ONLY mới trước khi chạy thật (migration xoá dòng)
- Preflight / Verify / Rollback: database/scripts/023-verify-purge-sample-customers.sql / database/scripts/024-rollback-purge-sample-customers.sql
- Pre-check 2026-11-04 ~10:50Z: VERIFY_STATUS=NOT_APPLIED (10 khách hàng khác, TVGPTA=1).
- Dry-run (một transaction, sqlcmd -b, kiểm tra @@TRANCOUNT sau mỗi batch): xoá 10 liên hệ + 10 khách hàng → verify PASS → rollback khôi phục 10 + 10 → verify NOT_APPLIED → ROLLBACK, DRYRUN_TRANCOUNT=0; pre-check lặp lại không đổi.
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
