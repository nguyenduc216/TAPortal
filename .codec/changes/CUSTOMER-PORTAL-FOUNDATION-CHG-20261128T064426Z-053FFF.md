# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261128T064426Z-053FFF

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261128T064426Z-053FFF`
- Created UTC: 2026-11-28T06:44:26.105759+00:00
- Tiêu đề: E-invoice environment tiles: production green, demo amber
- Trạng thái: PLANNED

## Yêu cầu và phạm vi
Operator (2026-11-28): thẻ môi trường Chính thức và Demo trên trang Hóa đơn điện tử phải khác màu hẳn để nhìn là phân biệt, ngoài nhãn trạng thái.
- Chính thức: nền xanh lá nhạt, vạch trái xanh lá, icon khiên. Demo: nền vàng nhạt, viền nét đứt, vạch trái sọc vàng, icon cảnh báo.
- Thẻ đang dùng: viền 2px cùng màu môi trường. Dòng "Hệ thống" ở phần tài khoản hiện nhãn màu tương ứng.
- Sửa lỗi: viền thẻ đang dùng dùng biến --cp-green-600 không tồn tại nên hiện màu đen.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TACustomerPortal.Web/Views/EInvoice/Index.cshtml`, `src/TACustomerPortal.Web/wwwroot/css/customer.css`
- MODIFY `tests/TACustomerPortal.Tests/SafeEasyInvoiceTests.cs`

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path: không có
- Thứ tự chạy:
- Preflight / Verify / Rollback:
- Execution: NOT_EXECUTED

## Test và kết quả
- Safe tests 873 PASS.

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
