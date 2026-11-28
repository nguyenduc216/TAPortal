# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261128T095103Z-2AB420

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261128T095103Z-2AB420`
- Created UTC: 2026-11-28T09:51:03.303705+00:00
- Tiêu đề: SoftDreams account card: environment tiles replace the summary, active tile can change its account
- Trạng thái: PLANNED

## Yêu cầu và phạm vi
Operator (2026-11-28), sau khi kết nối được môi trường chính thức SoftDreams:
- Bỏ phần tóm tắt tài khoản (phần mềm, MST, tên đăng nhập, hệ thống, kết nối lúc) vì thẻ môi trường đã hiện tài khoản. Chỉ áp dụng cho SoftDreams; MISA/Viettel không có thẻ môi trường nên giữ phần tóm tắt.
- Thẻ môi trường đang dùng cũng có nút "Đổi tài khoản / mật khẩu" (mở form kết nối của môi trường đó, giữ mẫu hóa đơn của môi trường).
- Nhãn "Môi trường" kèm logo và tên nhà cung cấp; nút "Gỡ kết nối" chuyển xuống dưới thẻ môi trường.

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
- Safe tests 874 PASS.

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
