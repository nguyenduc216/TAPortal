# Change TAPORTAL-SYSTEM-AUDIT-CHG-20261106T054921Z-5B484C

- Feature ID: `TAPORTAL-SYSTEM-AUDIT`
- Change ID: `TAPORTAL-SYSTEM-AUDIT-CHG-20261106T054921Z-5B484C`
- Created UTC: 2026-11-06T05:49:21.072077+00:00
- Tiêu đề: Thong bao dang toast giua tren man hinh (Admin + Customer Portal)
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator (2026-11-06): thông báo trên Admin portal và Customer Portal khó nhận biết vì chỉ là một dòng ở đầu nội dung; làm toast ở trên cùng, chính giữa màn hình.

Trong phạm vi:
- `ta-shell.js` (bản dùng chung, giống hệt ở hai app): phần tử `[data-ta-toast]` được chuyển vào ngăn `.ta-toasts` cố định ở đỉnh, giữa màn hình; có nút đóng (×), Escape đóng tất cả; tự ẩn sau 5 giây (thành công) / 9 giây (lỗi, role=alert), tạm dừng khi rê chuột hoặc focus. Chạy cả khi trang tải lần đầu và sau soft navigation (`ta:load`). Không có JS thì thông báo vẫn hiện tại chỗ như trước.
- Layout Admin và Customer Portal: thông báo TempData Success/Error có `data-ta-toast` (Admin bỏ nút đóng Bootstrap, nút đóng do toast thêm).
- CSS toast riêng cho từng app (`ta-design.css`, `customer.css`): giữ màu thành công/lỗi hiện có, đổ bóng, hiệu ứng trượt xuống; điện thoại rộng gần hết màn hình; tôn trọng prefers-reduced-motion.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TACustomerPortal.Web/wwwroot/js/ta-shell.js`, `src/TAPortal.Web/wwwroot/js/ta-shell.js`
- MODIFY `src/TACustomerPortal.Web/Views/Shared/_Layout.cshtml`, `src/TAPortal.Web/Views/Shared/_Layout.cshtml`
- MODIFY `src/TACustomerPortal.Web/wwwroot/css/customer.css` (chỉ khối toast), `src/TAPortal.Web/wwwroot/css/ta-design.css`
- MODIFY `tests/TACustomerPortal.Tests/SafeUiDesignSystemTests.cs` (FlashMessagesAreRaisedAsTopCentreToasts)

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Không có migration.
- Execution: NOT_APPLICABLE

## Test và kết quả
- Kiểm tra giao diện bằng trang thử dùng đúng CSS/JS của hai app (1280px và 590px): toast nằm giữa phía trên, rộng 560px (hẹp: vừa màn hình), thành công tự ẩn sau 5 giây.
- Build/Safe tests: chạy trong audit của Deploy-TARemote (xem report).

## Publish
- invoice (TACustomerPortal.Web), portal (TAPortal.Web). Xem report.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: xem report
- PR:
- Merge: xem report
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Commit chỉ gồm phần toast; thay đổi chưa commit của phiên khác trong cùng checkout (logo, trang thanh toán công khai, migration 024, cùng `customer.css`/`_Layout.cshtml`) không bị đưa vào và không bị sửa.
- Trang thanh toán công khai /pay (đang làm ở phiên khác) chưa dùng toast.
