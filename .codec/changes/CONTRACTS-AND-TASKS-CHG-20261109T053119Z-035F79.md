# Change CONTRACTS-AND-TASKS-CHG-20261109T053119Z-035F79

- Feature ID: `CONTRACTS-AND-TASKS`
- Change ID: `CONTRACTS-AND-TASKS-CHG-20261109T053119Z-035F79`
- Created UTC: 2026-11-09T05:31:19.514793+00:00
- Tiêu đề: Tra cuu MST va goi y ma khach hang (Admin)
- Trạng thái: DEPLOYED

## Yêu cầu và phạm vi
Operator (2026-11-09), ảnh form Thêm khách hàng:
1. "phần mã số thuế thêm button tìm kiếm có thể tìm ra tên khách, tên người đại diện, số điện thoại, địa chỉ, email, và nếu được tìm luôn hình logo để lưu".
2. "Mã khách hàng có thể tự động dựa vào tên khách hàng để tự gen".

Đã làm:
- Nút "Tra cứu" cạnh Mã số thuế (Admin): điền tên + địa chỉ, hiện tình trạng NNT. Nguồn: dịch vụ VietQR đã dùng ở Customer Portal (dữ liệu tổng hợp từ Cục Thuế), chuyển `TaxCodeLookup` sang TAPortal.Shared để hai app dùng chung; chỉ mã số thuế rời server, có cache.
- Nguồn công khai không có người đại diện, điện thoại, email, logo: các ô này vẫn nhập tay (logo: tab Logo của khách hàng đã có).
- Mã khách hàng (khi tạo mới) tự gợi ý theo tên, kiểu mã đang dùng (ADHOME, AGRISEA…): bỏ dấu, bỏ loại hình/ngành chung và tỉnh ở cuối, không trùng (thêm 2, 3…); nhân viên gõ mã riêng thì không ghi đè.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `src/TAPortal.Web/Controllers/CustomerTaxLookupActions.cs`, `src/TAPortal.Web/Services/CustomerCodeSuggest.cs`, `tests/TACustomerPortal.Tests/SafeAdminCustomerLookupTests.cs`
- MOVE `src/TACustomerPortal.Web/Services/TaxCodeLookup.cs` → `src/TAPortal.Shared/TaxCodeLookup.cs` (namespace TAPortal.Shared; thêm TaxStatus, InternationalName)
- MODIFY `src/TAPortal.Web/Program.cs`, `src/TAPortal.Web/Services/CustomerAdminStore.cs`, `src/TAPortal.Web/Views/Customers/Edit.cshtml`, `src/TAPortal.Web/wwwroot/js/ta-portal.js`, `src/TAPortal.Web/wwwroot/css/ta-admin.css`, `src/TACustomerPortal.Web/Controllers/ContactsController.cs`, `src/TACustomerPortal.Web/Controllers/PayController.cs`, `tests/TACustomerPortal.Tests/SafeCustomerSignupTests.cs`, `tests/TACustomerPortal.Tests/SafeCustomerPayPageTests.cs`

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Không có migration.
- Execution: NOT_APPLICABLE

## Test và kết quả
- Release build 0 lỗi; Safe tests 606/606.

## Publish
- portal, invoice. Xem report.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: 4ff7d08
- PR:
- Merge: fast-forward main
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Nguồn công khai không có người đại diện, điện thoại, email, logo. Chưa xem trực tiếp trên production.
