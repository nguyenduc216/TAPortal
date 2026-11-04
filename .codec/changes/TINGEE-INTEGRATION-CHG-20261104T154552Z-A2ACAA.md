# Change TINGEE-INTEGRATION-CHG-20261104T154552Z-A2ACAA

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261104T154552Z-A2ACAA`
- Created UTC: 2026-11-04T15:45:52.403786+00:00
- Tiêu đề: Tingee Sandbox: thử Bank Link SDK (create-bank-link-session, popup/nhúng, callback)
- Trạng thái: DEPLOYED

## Yêu cầu và phạm vi
Operator (2026-11-04): "Thêm thử SDK" — thử Bank Link SDK của Tingee (màn hình liên kết ngân hàng do Tingee hiển thị) trong Tingee Sandbox của Admin trước khi cân nhắc dùng cho khách hàng. Mục tiêu: xem màn hình có lộ tên/logo Tingee không (quy tắc trung lập đối tác ở Customer Portal) và thử `bank-config` cho tài khoản doanh nghiệp.

Tài liệu: https://developers.tingee.vn/docs/bank-link/create-session/ và /ui-sdk/.

Trong phạm vi:
- `ITingeeApi.CreateBankLinkSessionAsync` → `POST /v1/create-bank-link-session` (type bank-link/bank-config, bankName = mã ngân hàng, bankAccountType, metadata; merchantId theo Direct/Master như các API khác). Trả URL SDK.
- Admin Tingee Sandbox mục 5: form chọn loại phiên, ngân hàng, loại tài khoản, popup/nhúng; action `BankLinkSession` (POST, antiforgery, JSON). Chỉ merchant con đã gắn khách hàng; giá trị ngoài danh mục bị bỏ; URL SDK ngoài `https://*.tingee.vn` bị từ chối. Script SDK theo môi trường API (production/UAT).
- JS: tải `tingee-bank-link.js` một lần, `Setup` + `Url.Open`, liệt kê mọi callback; submit bắt ở capture phase để không bị soft navigation post form.
- Không lưu DB; kết quả qua callback và webhook bank-link (mục 4).

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TAPortal.Integrations/Tingee/TingeeApi.cs`
- MODIFY `src/TAPortal.Web/Controllers/TingeeSandboxController.cs`
- MODIFY `src/TAPortal.Web/Views/TingeeSandbox/Index.cshtml`
- MODIFY `src/TAPortal.Web/wwwroot/js/ta-tingee-sandbox.js`
- MODIFY `tests/TACustomerPortal.Tests/SafeAdminTingeeSandboxTests.cs` (2 test mới + fake)
- MODIFY `tests/TACustomerPortal.Tests/SafeAdminTingeeTests.cs`, `SafeCustomerPaymentsTests.cs` (fake implement method mới)

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Execution: NOT_APPLICABLE (không có thay đổi SQL)

## Test và kết quả
- Safe tests: 400/400 pass.
- Tingee thật: NOT_VERIFIED (cần client có scope Bank).

## Publish
- Project: TAPortal.Web (portal), deploy cùng change TINGEE-INTEGRATION-CHG-20261104T124547Z-45D0B5 (e-invoice Sandbox). Xem report.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: 8729d4c
- PR:
- Merge: PENDING_APPROVAL
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Màn hình SDK do Tingee hiển thị (tên miền tingee.vn) — chưa dùng cho khách hàng cho đến khi xác nhận không lộ thương hiệu đối tác.
- Webhook bank-link mới chỉ lưu inbox, chưa ghi vào CustomerBankAccounts.
