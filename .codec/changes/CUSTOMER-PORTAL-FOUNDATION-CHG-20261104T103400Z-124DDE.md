# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261104T103400Z-124DDE

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261104T103400Z-124DDE`
- Created UTC: 2026-11-04T10:34:00.041145+00:00
- Tiêu đề: Xoá 10 khách hàng mẫu KH0001-KH0010 (giữ TVGPTA), có bảng lưu và rollback
- Trạng thái: EXECUTED

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
- Gates trên HEAD cb978c0 (worktree sạch): restore OK, build Release 0 lỗi 0 cảnh báo, Safe tests 353/353.
- Backup COPY_ONLY + CHECKSUM: C:\Program Files\Microsoft SQL Server\MSSQL12.MSSQLSERVER2014\MSSQL\Backup\TAPortalSys-predeploy-20261104T103823Z.bak (5,1 MB; msdb: checksums=1, damaged=0). RESTORE VERIFYONLY bị từ chối vì TA_User không có quyền CREATE DATABASE (Msg 262), nên kiểm tra qua msdb và file trên đĩa; còn bản 20261103T143226Z.
- Chạy thật 2026-11-04 10:38Z: xoá 10 liên hệ + 10 khách hàng; verify 023 VERIFY_STATUS=PASS (TVGPTA giữ, 0 khách hàng khác, bảng lưu 10/10). Portal health 200.
- Execution: EXECUTED

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
- Bảng lưu `dbo.Backup20261104_Customers` / `dbo.Backup20261104_CustomerContacts` giữ trong TAPortalSys; khôi phục bằng `database/scripts/024-rollback-purge-sample-customers.sql`. Xoá hai bảng này khi operator xác nhận không cần nữa (cần migration riêng).
