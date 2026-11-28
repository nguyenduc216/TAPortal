# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261128T045242Z-F11047

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261128T045242Z-F11047`
- Created UTC: 2026-11-28T04:52:42.975664+00:00
- Tiêu đề: E-invoice: provider logos on the provider cards
- Trạng thái: PLANNED

## Yêu cầu và phạm vi
Operator (2026-11-28): tìm logo các đối tác hóa đơn và thêm vào thẻ chọn phần mềm ở trang Hóa đơn điện tử.

Quyết định:
- Lấy biểu tượng (logo mark) từ trang chính thức của từng hãng, dùng chung kích thước vuông trên thẻ:
  - MISA meInvoice: `meInvoice.svg` từ meinvoice.vn; đã kiểm không có script.
  - Viettel S-Invoice: phần biểu tượng chữ "S" cắt từ banner S-Invoice trên sme.viettel.vn, thu về 64×64.
  - SoftDreams EasyInvoice: biểu tượng trang (favicon) của easyinvoice.vn, 40×40.
- File nằm trong `wwwroot/images/einvoice/{mã nhà cung cấp}`; hiện trên thẻ chọn và ở dòng "Phần mềm".

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `src/TACustomerPortal.Web/wwwroot/images/einvoice/misa.svg`, `s-invoice.png`, `softdreams.png`
- MODIFY `src/TACustomerPortal.Web/Models/EInvoiceModels.cs` (InvoiceText.Logo), `Views/EInvoice/Index.cshtml`, `wwwroot/css/customer.css`
- MODIFY `tests/TACustomerPortal.Tests/SafeCustomerEInvoiceTests.cs`

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Không có migration.
- Execution: NOT_APPLICABLE

## Test và kết quả
- Build 0 lỗi. Safe tests 872 PASS (test mới ProviderCardsShowEachProvidersLogo).

## Publish
- Project: TACustomerPortal.Web
- Destination (chỉ từ config):
- Trạng thái: NOT_PUBLISHED
- Artifact SHA256:
- Smoke test:

## Git
- Branch: feature/order-details-popups
- Commit SHA:
- PR:
- Merge: PENDING_APPROVAL
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
