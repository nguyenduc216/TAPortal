# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261112T054901Z-6AAF00

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261112T054901Z-6AAF00`
- Created UTC: 2026-11-12T05:49:01.559612+00:00
- Tiêu đề: Logo mới Hóa đơn tự động (vòng hóa đơn điện tử)
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator (2026-11-12): thay logo của hệ thống hóa đơn điện tử (Hóa đơn tự động) bằng ảnh minh họa vòng hóa đơn điện tử (hóa đơn → ngân hàng → cơ quan thuế) do operator gửi.
Đã làm:
- Từ ảnh gốc 2000×2000: cắt vuông, làm trắng nền hơi xám, xuất `logo.png` (256 px, thanh bên và các trang đăng nhập/đăng ký/quên/đặt lại mật khẩu, hộp kết nối lại), `favicon-32x32.png`, `apple-touch-icon.png` (180 px, biểu tượng khi thêm vào màn hình chính) và `og-share.png` (1200×630, ảnh xem trước khi gửi link thanh toán qua Zalo/Messenger).
- Bỏ `logo.svg` cũ. Portal quản trị giữ logo T.A.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `src/TACustomerPortal.Web/wwwroot/images/logo.png`, `src/TACustomerPortal.Web/wwwroot/images/apple-touch-icon.png`
- MODIFY `src/TACustomerPortal.Web/wwwroot/images/favicon-32x32.png`, `src/TACustomerPortal.Web/wwwroot/images/og-share.png`, `src/TACustomerPortal.Web/Views/Shared/_Layout.cshtml`, `src/TACustomerPortal.Web/Views/Pay/Index.cshtml`, `src/TACustomerPortal.Web/Views/Account/{Login,Register,ForgotPassword,ResetPassword}.cshtml`, `tests/TACustomerPortal.Tests/SafeUiDesignSystemTests.cs`
- DELETE `src/TACustomerPortal.Web/wwwroot/images/logo.svg`

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path: không có
- Thứ tự chạy: không áp dụng
- Preflight / Verify / Rollback: không áp dụng
- Execution: NOT_APPLICABLE

## Test và kết quả
- Release build 0 lỗi; Safe tests 654/654 (TheCustomerPortalShowsItsOwnLogo thay cho TheCustomerPortalShowsItsVectorLogo: kiểm tra PNG và kích thước, không còn view nào dùng logo.svg).

## Publish
- invoice. Xem report.

## Git
- Branch: ui/hoadon-logo
- Commit SHA: xem report
- PR:
- Merge: xem report
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Ảnh minh họa nhiều chi tiết nên ở cỡ 32 px (favicon) chỉ còn nhận ra vòng màu; nếu cần favicon rõ hơn có thể làm bản đơn giản riêng.
- Zalo/Messenger lưu đệm ảnh xem trước của link cũ một thời gian.
