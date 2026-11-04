# Change TINGEE-INTEGRATION-CHG-20261104T063712Z-0516FA

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261104T063712Z-0516FA`
- Created UTC: 2026-11-04T06:37:12.278468+00:00
- Tiêu đề: Sandbox: QR card beside sections 1-2; short webhook addresses with copy buttons
- Trạng thái: IMPLEMENTED / TESTS_PASS

## Yêu cầu và phạm vi
Operator (2026-11-04): đưa mục 3 (Tạo QR thử) lên cùng hàng với mục 1 và 2; các URL webhook khai báo gọn lại (cắt ngắn, thêm nút copy; bấm copy thì icon đổi sang xanh lá và hiện tooltip "Đã copy").
- QR thành cột thứ 3 của lưới (≥1200px; nhỏ hơn thì xuống hàng theo lưới có sẵn); form xếp dọc cho vừa cột.
- Webhook: hiện "…/hooks/tingee/<kind>", địa chỉ đầy đủ trong title và được chép; nút copy dùng icon mới `copy` (Tabler, thêm vào `UiIcon`), đổi sang dấu check xanh 2 giây kèm tooltip Bootstrap "Đã copy"; có fallback execCommand khi không có Clipboard API.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TAPortal.Shared/UiIcon.cs` (icon `copy`) — Shared nên deploy cả portal và invoice.
- MODIFY `src/TAPortal.Web/Views/TingeeSandbox/Index.cshtml`, `src/TAPortal.Web/wwwroot/js/ta-tingee-sandbox.js`.
- MODIFY tests `SafeAdminTingeeSandboxTests.cs`.

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path:
- Thứ tự chạy:
- Preflight / Verify / Rollback:
- Execution: NOT_APPLICABLE

## Test và kết quả
- Build Release 0 lỗi; Safe tests 351/351 (working tree). Giao diện chưa xem bằng trình duyệt trước deploy.

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
