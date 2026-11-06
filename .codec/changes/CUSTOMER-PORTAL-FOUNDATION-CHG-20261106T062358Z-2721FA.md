# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T062358Z-2721FA

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T062358Z-2721FA`
- Created UTC: 2026-11-06T06:23:58Z
- Tiêu đề: Logo ở danh sách khách hàng, thanh bên và Admin portal
- Trạng thái: DEPLOYED

## Yêu cầu và phạm vi
Operator 2026-11-06: bổ sung 2 việc còn lại của change D0B894: (1) logo hiện ở danh sách khách hàng và thanh bên Customer Portal;
(2) nhân viên T.A xem/sửa logo trong Admin portal.

Quyết định kỹ thuật:
- Phần logo dùng chung chuyển sang `TAPortal.Integrations/Branding/Logos.cs` (LogoImage, ILogoStore, LogoUpload, SqlLogoStore, AddLogos)
  để Admin và Customer Portal cùng dùng bảng `dbo.CustomerLogos` (migration 024, đã chạy). Không có migration mới.
- Phiên bản logo doanh nghiệp được cache 1 phút mỗi tiến trình (hiện ở mọi trang qua thanh bên); thay đổi tại một site xoá cache site đó,
  site kia cập nhật trong tối đa 1 phút. Lỗi đọc logo không làm hỏng layout (hiện chữ viết tắt / biểu tượng).
- Admin: Customer Workspace → tab "Hồ sơ doanh nghiệp" có thẻ "Logo doanh nghiệp" (tải lên / xoá, quyền CRM.CUSTOMERS.UPDATE, ghi log);
  header mọi tab của workspace hiện logo thay biểu tượng toà nhà. Logo khách hàng của doanh nghiệp vẫn do doanh nghiệp quản lý.
- Customer Portal: danh sách Khách hàng có logo (hoặc chữ viết tắt) cạnh tên; thanh bên hiện logo doanh nghiệp thay chữ viết tắt.

## File thay đổi
- ADD `src/TAPortal.Integrations/Branding/Logos.cs`; ADD `src/TAPortal.Web/Controllers/CustomerLogoActions.cs`
- MODIFY `src/TACustomerPortal.Web/Services/OrderPageStore.cs` (bỏ phần logo), `Program.cs`, `Controllers/PayController.cs`, `ProfileController.cs`,
  `ContactsController.cs` (chỉ Index + using), `Models/PaymentModels.cs` (ContactsVm.Logos), `Views/Contacts/Index.cshtml`, `Views/Shared/_Layout.cshtml`,
  `Views/_ViewImports.cshtml`, `wwwroot/css/customer.css` (2 khối)
- MODIFY `src/TAPortal.Web/Program.cs`, `Views/Customers/Profile.cshtml`, `Views/Customers/_Workspace.cshtml`, `wwwroot/css/ta-admin.css`
- MODIFY tests: SafeCustomerPayPageTests, SafeCustomerProfileTests (StaffSetAndRemoveTheCompanyLogoShownInTheWorkspaceHeader), SafeCustomerPaymentsTests (đăng ký store), SafePortalTests

## Migration SQL
- Không có (dùng bảng của migration 024, EXECUTED trong change D0B894).

## Test và kết quả
- Release build 0 errors; Safe tests 478/478 PASS (cùng thay đổi chưa commit của session khác trong checkout); codec unittest OK; git diff --check OK.
- Gate trên commit sạch bcec7af (worktree riêng, trong Deploy-TARemote): build 0 errors, Safe 478/478 PASS.

## Publish
- DEPLOYED via `Deploy-TARemote.ps1 -Target invoice,portal,webhooks` từ worktree sạch (audit PASS, Release build 0 errors, Safe 478/478), source bcec7af, 2026-11-06T06:40Z:
  - invoice / TACustomerPortal.Web: artifact 6e798d983b3a946eabf2cea2ad3600ab2c7aaf480e06d02b3fcedae2e3c5e3cd, package C6D49B0A6641C9316EBFD99E5B52A0F38FC5A2C843EFAB7C394F6515AC2C5741, health 200
  - portal / TAPortal.Web: artifact 41ab03ad7707d6f2af101ae06d5cb7686bea4cdd33c2249d63896de32653585c, package E59B7BBD8E5D9472A28C79C9F1FF9C2E1617772495EF4584F9F995CD5C3AD7B6, health 200
  - webhooks / TAPortal.Webhooks: artifact 8085b273fdd9fa1714118e132bbd92dd261547d51ef7a67a6690eba78c80ee2a, package 2E9D841AB57EFCD0CAA4DCBCFFF3B24B3ECFB9A0D966FDC8E028C35A68EDB5A2, health 200

## Git
- Branch: feature/customer-portal-mvp01
- Source commit: bcec7af
- Chỉ commit phần của change này; thay đổi chưa commit của session khác trong checkout (TaxCodeLookup.cs, Contacts/Edit.cshtml, ApplyBuyerType/PhoneDigits,
  .cp-form-grid--3, test số điện thoại) được giữ nguyên, không stage.

## Rủi ro, quyết định và việc còn lại
- Logo cache 1 phút: sau khi Admin đổi logo, Customer Portal thấy sau tối đa 1 phút (và ngược lại).
