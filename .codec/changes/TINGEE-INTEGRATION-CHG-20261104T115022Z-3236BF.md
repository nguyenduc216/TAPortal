# Change TINGEE-INTEGRATION-CHG-20261104T115022Z-3236BF

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261104T115022Z-3236BF`
- Created UTC: 2026-11-04T11:50:22.421170+00:00
- Tiêu đề: Customer Portal: logo ngân hàng trên trang chọn ngân hàng liên kết
- Trạng thái: DEPLOYED

## Yêu cầu và phạm vi
Operator (2026-11-04): "Kiểm tra xem logo ngân hàng đã làm xong chưa thì commit và deployed luôn". Phần việc do một agent khác làm, chưa commit; kiểm tra thấy hoàn chỉnh nên commit theo yêu cầu của operator.
- Trang Customer Portal → Tài khoản ngân hàng → Liên kết: mỗi ngân hàng hiện logo `wwwroot/images/banks/<mã ngân hàng>.png` (13 ngân hàng của danh mục; PNG 831×311 / 811×331, đã xem đúng logo) thay cho icon chung; thẻ chọn xếp logo trên tên.
- Test `SafeCustomerBankLogoTests.EveryCatalogBankHasALogo`: ngân hàng thêm vào danh mục mà thiếu logo thì test đỏ.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `src/TACustomerPortal.Web/wwwroot/images/banks/{acb,bidv,coopbank,ctg,mbb,msb,ocb,pgb,stb,tpb,vcb,vib,vpb}.png`, `tests/TACustomerPortal.Tests/SafeCustomerBankLogoTests.cs`.
- MODIFY `src/TACustomerPortal.Web/Views/Banks/Add.cshtml`, `src/TACustomerPortal.Web/wwwroot/css/customer.css` (deploy invoice).

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path:
- Thứ tự chạy:
- Preflight / Verify / Rollback:
- Execution: NOT_APPLICABLE

## Test và kết quả
- Safe tests 388/388 (working tree); logo + payments 33/33.

## Publish
- invoice (TACustomerPortal.Web → TA.Invoice): DEPLOYED 2026-11-04 18:52 (UTC+7), commit 63da984, artifact SHA256 690d742b976df9baae2d42a61910176e5810384f92085b5490293e2a62866d00, package AC359C3C0F0520559B5563A625D05D333B11C41D7F65D6CC168D030B14D29CA0; backup C:\TADeploy\backups\invoice-20261104-185221; health HTTP 200.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: 63da984 (source)
- PR:
- Merge: PENDING_APPROVAL
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
