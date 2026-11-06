# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T042042Z-D6E634

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T042042Z-D6E634`
- Created UTC: 2026-11-06T04:20:42.488791+00:00
- Tiêu đề: Shell: button formaction/formtarget (MISA templates, draft preview)
- Trạng thái: DEPLOYED

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
- Destination (chỉ từ config): ta-server TA.Invoice (H:\WEBDEPLOYED\T.A\customer-portal), TA.Portal (H:\WEBDEPLOYED\T.A\portal_released)
- Trạng thái: DEPLOYED 2026-11-06 11:32 (giờ VN), commit 876eb24, backup server invoice-20261106-113154 / portal-20261106-113207
- Artifact SHA256: invoice 3823ff5d9022199045a96fee94bc7e7471a79cb1546705807ecb88ba73b26937; portal a07dd61c534012a33a29368b2236920be2726718166a62196e424ebd73c2cdd7
- Smoke test: health HTTP 200 cả hai; ta-shell.js trên hoadontudong.vn và portal.tuvangiaiphapta.vn chứa xử lý formaction; customer.js chứa xử lý formtarget.
- Ghi chú: audit dải commit portal (4a89199..) gồm migration 021/022 đã EXECUTED (be5f2f0, 27e2400; kiểm tra chỉ-đọc cột/ bảng có trên TAPortalSys) ⇒ -MigrationsApplied.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: 876eb24
- PR:
- Merge: PENDING_APPROVAL
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Kết quả lấy mẫu hiển thị qua soft-nav với URL /EInvoice/Templates (POST-only); tải lại trang ở URL đó sẽ không hiện lại danh sách mẫu — mở lại /einvoice.
- Cần operator bấm lại "Lấy mẫu hóa đơn từ MISA" sau deploy để xác nhận với tài khoản MISA thật.
