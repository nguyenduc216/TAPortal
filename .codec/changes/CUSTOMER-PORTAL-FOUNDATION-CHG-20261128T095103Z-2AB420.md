# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261128T095103Z-2AB420

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261128T095103Z-2AB420`
- Created UTC: 2026-11-28T09:51:03.303705+00:00
- Tiêu đề: SoftDreams account card: environment tiles replace the summary, active tile can change its account
- Trạng thái: DEPLOYED

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
- Project: TACustomerPortal.Web
- Destination (chỉ từ config): invoice (TA.Invoice)
- Trạng thái: DEPLOYED 2026-11-28 09:53Z, source commit 43c5ea5, `Deploy-TARemote.ps1 -Target invoice`, backup `C:\TADeploy\backups\invoice-20261128-165349`
- Artifact SHA256: 0e5751423ced75ab584f782ef4949c8e30d90761eb73947db86f390e263fe63c
- Smoke test: health HTTP 200

## Git
- Branch: feature/order-details-popups
- Commit SHA: 43c5ea5
- PR:
- Merge: fast-forward vào main
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
