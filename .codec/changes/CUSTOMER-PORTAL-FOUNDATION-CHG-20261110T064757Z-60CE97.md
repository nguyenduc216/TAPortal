# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261110T064757Z-60CE97

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261110T064757Z-60CE97`
- Created UTC: 2026-11-10T06:47:57.996909+00:00
- Tiêu đề: Sua dau ngoac thua lam trang thanh toan hien '} else {'
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator (2026-11-10, ảnh chụp production): trang thanh toán hiện chữ "} else {" giữa trang, kèm thông báo "Đơn hàng chưa có mã QR" và "Tổng tiền đơn hàng" ngay dưới mã QR đang mở.
Nguyên nhân: change CUSTOMER-PORTAL-FOUNDATION-CHG-20261110T060913Z-B1DDA6 để thừa một dấu `}` sau hộp ảnh QR trong `Views/Pay/Index.cshtml`, Razor đóng khối `if` sớm và in phần `else` ra thành chữ. Đã xóa dấu thừa; test trang thanh toán kiểm tra không còn "} else {" và không hiện khối "chưa có mã QR" khi QR đang mở.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TACustomerPortal.Web/Views/Pay/Index.cshtml`, `tests/TACustomerPortal.Tests/SafeCustomerPayPageTests.cs`

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path: không có
- Thứ tự chạy: không áp dụng
- Preflight / Verify / Rollback: không áp dụng
- Execution: NOT_APPLICABLE

## Test và kết quả
- Release build 0 lỗi; Safe tests 622/622; `git diff --check` sạch.

## Publish
- invoice. Xem report.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: xem report
- PR:
- Merge: xem report
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Bản trước (bd93fff) chạy production khoảng 10 phút với lỗi hiển thị này; chức năng thanh toán không bị ảnh hưởng.
