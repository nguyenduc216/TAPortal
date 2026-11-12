# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261112T150549Z-6358B3

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261112T150549Z-6358B3`
- Created UTC: 2026-11-12T15:05:49.343415+00:00
- Tiêu đề: Sign-in gold panel fills wide screens
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator (2026-11-12): trang đăng nhập trên màn hình rộng "vẫn còn nhiều khoảng trống" ở panel vàng; bên nền trắng thêm animation về ngân hàng, thuế, trao đổi giao dịch.

Đã làm:
- Từ 1200px: panel vàng tối đa 720px, nội dung thành một cột 560px ở giữa, tiêu đề lớn hơn, ba điểm mạnh thành ba ô ngang; minh họa phóng 1,22 lần khi màn hình cao từ 860px (màn thấp giữ cỡ cũ để không phải cuộn).
- Nền trắng (`_AuthBackdrop`): thêm 8 hình trôi — tòa ngân hàng, thẻ thuế (%), mũi tên trao đổi giao dịch (lắc qua lại), thẻ ngân hàng; tắt khi giảm chuyển động.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TACustomerPortal.Web/wwwroot/css/customer.css`, `src/TACustomerPortal.Web/Views/Account/_AuthBackdrop.cshtml`

## Migration SQL (KHÔNG tự chạy)
- Không có.

## Test và kết quả
- Safe tests 668/668; xem trên trình duyệt ở 1920×911 và 1366×768 (không tràn ngang, không cuộn dọc).

## Publish
- invoice. Xem report.

## Git
- Branch: feature/tx-sync
- Commit SHA: xem report
- PR:
- Merge: xem report
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Không.
