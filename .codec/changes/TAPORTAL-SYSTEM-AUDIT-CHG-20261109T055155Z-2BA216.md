# Change TAPORTAL-SYSTEM-AUDIT-CHG-20261109T055155Z-2BA216

- Feature ID: `TAPORTAL-SYSTEM-AUDIT`
- Change ID: `TAPORTAL-SYSTEM-AUDIT-CHG-20261109T055155Z-2BA216`
- Created UTC: 2026-11-09T05:51:55.105115+00:00
- Tiêu đề: Khung ket noi lai / dang cap nhat (logo trong vong xoay) cho Admin va Hoa don tu dong
- Trạng thái: DEPLOYED

## Yêu cầu và phạm vi
Operator (2026-11-09): trước đây khi server cập nhật/mất kết nối thì giao diện hiện khung loading (logo T.A) và cập nhật xong tự tải lại; hiện không còn, phải F5.
Nguyên nhân: khung đó thuộc bản Admin Blazor cũ (ReconnectModal + taportal-reconnect.js, commit 6f1257b); Admin đã chuyển sang MVC nên mất.
Đã làm (cả Admin và Hóa đơn tự động):
- GET /_build (ẩn danh, no-store) trả mã build; layout có `<meta name="ta-build">` kèm logo của từng site (Admin: ta-logo.png; Hóa đơn tự động: logo.svg — không hiện T.A).
- `wwwroot/js/ta-update.js` (giống hệt ở hai app): hỏi /_build mỗi 5 giây khi tab đang mở; không trả lời → khung "Đang kết nối lại" → "Hệ thống đang được cập nhật" (502/503/504) hoặc "Hệ thống đang gián đoạn"; có lại cùng build → "Kết nối đã khôi phục" tự đóng; build mới → "Máy chủ đã hoạt động trở lại" và tự tải lại (đang gõ dở form thì hiện thanh "Tải lại").
- ta-shell.js: điều hướng mềm gặp 502/503/504 hoặc mất mạng → hiện khung thay vì trang lỗi IIS.
- Gate đổi mật khẩu (Customer Portal) cho qua /_build.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `src/TAPortal.Shared/BuildInfo.cs`, `src/TAPortal.Web/wwwroot/js/ta-update.js`, `src/TACustomerPortal.Web/wwwroot/js/ta-update.js`
- MODIFY `src/TAPortal.Web/Program.cs`, `src/TACustomerPortal.Web/Program.cs`, `src/TAPortal.Web/Views/Shared/_Layout.cshtml`, `src/TACustomerPortal.Web/Views/Shared/_Layout.cshtml`, `src/TAPortal.Web/wwwroot/js/ta-shell.js`, `src/TACustomerPortal.Web/wwwroot/js/ta-shell.js`, `src/TACustomerPortal.Web/Security/CustomerSecurity.cs`, `tests/TACustomerPortal.Tests/SafeUiDesignSystemTests.cs`

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Không có migration.
- Execution: NOT_APPLICABLE

## Test và kết quả
- Release build 0 lỗi; Safe tests 607/607; thử trên trình duyệt với trang thử: tắt server → khung hiện; bật lại build mới → tự tải lại.

## Publish
- portal, invoice. Xem report.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: e1762d1
- PR:
- Merge: fast-forward main
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Trang mở trước bản này chưa có script nên lần deploy này chưa hiện khung; mỗi tab gọi /_build 5 giây/lần (rất nhẹ).
