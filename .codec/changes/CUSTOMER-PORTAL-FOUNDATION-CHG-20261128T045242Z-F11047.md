# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261128T045242Z-F11047

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261128T045242Z-F11047`
- Created UTC: 2026-11-28T04:52:42.975664+00:00
- Tiêu đề: E-invoice: provider logos on the provider cards
- Trạng thái: DEPLOYED

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
- Destination (chỉ từ config): invoice (TA.Invoice)
- Trạng thái: DEPLOYED 2026-11-28 04:55Z, source commit e527e88, `Deploy-TARemote.ps1 -Target invoice`, health HTTP 200
- Artifact SHA256: 2e205667b972a7c91c364568e7fa6245882b49d7fc7c1de96b7c241fc6fdc3d0 (package AADEA8AC595CE74FED0EE16832626334820A15BED8978B95279430754317A9BC)
- Backup trên server: `C:\TADeploy\backups\invoice-20261128-115523`
- Smoke test: health 200; ba file logo trả HTTP 200 trên app.hoadontudong.vn.

## Git
- Branch: feature/order-details-popups
- Commit SHA: e527e88
- PR:
- Merge: MERGED fast-forward vào main (sau deploy)
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
