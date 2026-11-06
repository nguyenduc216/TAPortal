# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T042042Z-D6E634

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T042042Z-D6E634`
- Created UTC: 2026-11-06T04:20:42.488791+00:00
- Tiêu đề: Shell: button formaction/formtarget (MISA templates, draft preview)
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator (2026-11-06): "Lấy mẫu hóa đơn từ MISA" và "Xem thử hóa đơn nháp" trên trang Hóa đơn điện tử (Customer Portal) không hoạt động.

Nguyên nhân (IIS log + TingeeApiLogs, đọc chỉ-đọc): cả hai nút gửi `POST /EInvoice/Save`, không có lệnh lấy mẫu/tạo nháp nào tới đối tác. `ta-shell.js` (gửi form ngầm) luôn dùng `action`/`method` của form và bỏ qua `formaction`/`formmethod`/`formtarget` của nút được bấm. Cùng lỗi với nút "Tạo nháp"/"Phát hành" của Admin → Hóa đơn Sandbox (cùng file).

Sửa: shell dùng `formaction`/`formmethod` của nút nếu có; nút có `formtarget` khác `_self` (mở tab mới) để trình duyệt gửi bình thường. `customer.js` không khóa nút trên trang khi form gửi sang tab mới.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TAPortal.Web/wwwroot/js/ta-shell.js`, `src/TACustomerPortal.Web/wwwroot/js/ta-shell.js` (giữ giống hệt nhau)
- MODIFY `src/TACustomerPortal.Web/wwwroot/js/customer.js`
- MODIFY `tests/TACustomerPortal.Tests/SafeUiDesignSystemTests.cs` (ShellSubmitHonoursButtonFormactionAndFormtarget)

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Không có migration.
- Execution: NOT_EXECUTED

## Test và kết quả
- `node --check` 2 file JS OK. `dotnet build TAPortal.sln -c Release`: 0 lỗi. Safe tests 468/468 pass. `test_codec.py` OK. `git diff --check` OK.
- `codec validate`: lỗi publish-path có sẵn từ trước (không liên quan change này).

## Publish
- Project: invoice (TACustomerPortal.Web), portal (TAPortal.Web) qua deploy/windows/Deploy-TARemote.ps1
- Destination (chỉ từ config):
- Trạng thái: NOT_PUBLISHED
- Artifact SHA256:
- Smoke test:

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA:
- PR:
- Merge: PENDING_APPROVAL
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Kết quả lấy mẫu hiển thị qua soft-nav với URL /EInvoice/Templates (POST-only); tải lại trang ở URL đó sẽ không hiện lại danh sách mẫu — mở lại /einvoice.
- Cần operator bấm lại "Lấy mẫu hóa đơn từ MISA" sau deploy để xác nhận với tài khoản MISA thật.
