# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261128T043400Z-BA4498

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261128T043400Z-BA4498`
- Created UTC: 2026-11-28T04:34:00.390336+00:00
- Tiêu đề: E-invoice: saved template list per provider, pick the default
- Trạng thái: DEPLOYED

## Yêu cầu và phạm vi
Operator (2026-11-28), trang Hóa đơn điện tử: thay vì chỉ một mẫu, có danh sách mẫu hóa đơn (trên demo hoặc chính thức), chọn mẫu nào làm mặc định thì hóa đơn dùng mẫu đó; danh sách đi theo nhà cung cấp.

Quyết định:
- SoftDreams EasyInvoice không có API lấy danh sách mẫu (chưa tìm thấy tài liệu), nên danh sách là các mẫu doanh nghiệp đã lưu. MISA/Viettel vẫn lấy mẫu qua API như cũ; mẫu đã lưu hiện khi chưa bấm lấy từ API.
- Bảng mới dbo.CustomerInvoiceTemplates (migration 070), tách theo nhà cung cấp và môi trường LIVE/DEMO (DEMO khi kết nối SoftDreams bản thử).
- Mẫu mặc định vẫn là CustomerInvoiceSettings.InvoicePattern/InvoiceSeries. Lưu cài đặt với mẫu mới thì mẫu được thêm vào danh sách. Mẫu có trước khi có danh sách vẫn hiện (không chép dữ liệu bằng migration).
- Xóa được mẫu không phải mặc định.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `database/migrations/070-customer-invoice-templates.sql`, `database/scripts/127-verify-customer-invoice-templates.sql`, `database/scripts/128-rollback-customer-invoice-templates.sql`
- MODIFY `src/TAPortal.Integrations/Tingee/OrderInvoices.cs` (SavedInvoiceTemplate, phương thức store), `SqlOrderInvoiceStore.cs`
- MODIFY `src/TACustomerPortal.Web/Controllers/EInvoiceController.cs` (lưu vào danh sách, DeleteTemplate), `Models/EInvoiceModels.cs`, `Views/EInvoice/Index.cshtml`, `wwwroot/css/customer.css`, `wwwroot/js/customer.js`
- MODIFY `tests/TACustomerPortal.Tests/SafeCustomerEInvoiceTests.cs`

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script: `database/migrations/070-customer-invoice-templates.sql`
- Thứ tự chạy: sau 069
- Preflight / Verify / Rollback: `127-verify-customer-invoice-templates.sql` / `128-rollback-customer-invoice-templates.sql`. Chỉ thêm 1 bảng, không đổi dòng nào.
- Execution: EXECUTED 2026-11-28 04:38:51Z qua `Invoke-TAMigrations.ps1 -Plan "070:127:128"` tại HEAD 2452d5a. Pre-check NOT_APPLIED; dry-run afterMigration=PASS, afterRollback=NOT_APPLIED, trancount 0; backup: dùng bản full gần nhất (913 phút); gates 870/870 PASS; real run VERIFY_STATUS=PASS. Log: `.codec/reports/CUSTOMER-PORTAL-FOUNDATION-CHG-20261128T043400Z-BA4498-migrations-20261128T043631Z.log`.

## Test và kết quả
- Build 0 lỗi. Test mới SavedTemplatesListThePerProviderTemplatesAndPickTheDefault.

## Publish
- Project: TACustomerPortal.Web (Integrations đổi; webhooks không dùng phần mới)
- Destination (chỉ từ config): invoice (TA.Invoice)
- Trạng thái: DEPLOYED 2026-11-28 04:42Z, source commit 2452d5a, `Deploy-TARemote.ps1 -Target invoice -MigrationsApplied`, health HTTP 200
- Artifact SHA256: 7da0cb62907c819c902b7510949dd4e9e2943039770a43f10ee9d4acdccb9d4d (package 3693A82EB502555142D899143228CD0202DDC88FFF4AA538971561000F60102C)
- Backup trên server: `C:\TADeploy\backups\invoice-20261128-114217`
- Smoke test: health 200; audit gates PASS (870). Portal/webhooks không deploy: chỉ thêm phương thức mặc định ở Integrations, không đổi hành vi.

## Git
- Branch: feature/order-details-popups
- Commit SHA: 2452d5a
- PR:
- Merge: MERGED fast-forward vào main (sau deploy)
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Nếu SoftDreams cung cấp API danh sách mẫu, có thể lấy tự động thay vì nhập tay.
