# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261128T064426Z-053FFF

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261128T064426Z-053FFF`
- Created UTC: 2026-11-28T06:44:26.105759+00:00
- Tiêu đề: E-invoice environment tiles: production green, demo amber
- Trạng thái: DEPLOYED

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
- Project: TACustomerPortal.Web
- Destination (chỉ từ config): invoice (TA.Invoice)
- Trạng thái: DEPLOYED 2026-11-28 06:48Z, source commit eb750a2, `Deploy-TARemote.ps1 -Target invoice`, backup `C:\TADeploy\backups\invoice-20261128-134805`
- Artifact SHA256: 202a470be31a23caacfd1493c49f5ed4452818c0ee4377d4888e11a7d501c7a8
- Smoke test: health HTTP 200

## Git
- Branch: feature/order-details-popups
- Commit SHA: eb750a2
- PR:
- Merge: fast-forward vào main
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Chỉ đổi giao diện (CSS và nhãn). Chưa xem trên giao diện thật vì cần đăng nhập.
