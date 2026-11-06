# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T044512Z-10B30D

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T044512Z-10B30D`
- Created UTC: 2026-11-06T04:45:12.341599+00:00
- Tiêu đề: MISA template id (GUID): InvoicePattern varchar(50)
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator (2026-11-06): "Xem thử hóa đơn nháp" báo "Nhà cung cấp hóa đơn từ chối (mã 400): Misa API error".

Nguyên nhân (TingeeApiLogs, đọc chỉ-đọc): invoice-templates trả mẫu với invoicePattern là GUID 36 ký tự (mã mẫu MISA); cài đặt đang lưu mẫu số "1" gõ tay nên create (draft) bị MISA từ chối. Không lưu được GUID vì cột InvoicePattern varchar(20) và validation 20 ký tự (Customer Portal và Admin Sandbox).

Sửa: InvoicePattern varchar(50) (migration 023); validation mẫu số 1–50 ký tự (Customer Portal input/choice, Admin Sandbox StringLength/maxlength); danh sách mẫu hiển thị "Mẫu trên MISA" thay vì GUID; gợi ý dưới ô Mẫu số.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `database/migrations/023-einvoice-template-id.sql`, `database/scripts/035-verify-einvoice-template-id.sql`, `database/scripts/036-rollback-einvoice-template-id.sql`
- MODIFY `src/TACustomerPortal.Web/Models/EInvoiceModels.cs`, `Controllers/EInvoiceController.cs`, `Views/EInvoice/Index.cshtml`
- MODIFY `src/TAPortal.Web/Models/TingeeInvoiceSandboxModels.cs`, `Views/TingeeInvoiceSandbox/Index.cshtml`
- MODIFY `tests/TACustomerPortal.Tests/SafeCustomerEInvoiceTests.cs` (AMisaTemplateIdentifiedByAGuidCanBePickedAndSaved)

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script: database/migrations/023-einvoice-template-id.sql (nới cột nullable, không sửa dòng nào)
- Thứ tự chạy: sau 022
- Preflight / Verify / Rollback: 035-verify (NOT_APPLIED khi varchar(20), PASS khi varchar(50)) / 036-rollback (từ chối khi có giá trị > 20 ký tự)
- Execution: EXECUTED 2026-11-06 04:47:36Z (Invoke-TAMigrations: precheck NOT_APPLIED, dry-run PASS/NOT_APPLIED, backup đầy đủ 810 phút, gates 469/469, real run VERIFY_STATUS=PASS); log .codec/reports/CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T044512Z-10B30D-migrations-20261106T044540Z.log

## Test và kết quả
- `dotnet build TAPortal.sln -c Release`: 0 lỗi. Safe tests 469/469 pass.

## Publish
- Project:
- Destination (chỉ từ config):
- Trạng thái: NOT_PUBLISHED
- Artifact SHA256:
- Smoke test:

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA:
- PR:
- Merge: PENDING_APPROVAL
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Sau deploy, doanh nghiệp phải bấm "Lấy mẫu hóa đơn từ MISA", chọn mẫu rồi Lưu (giá trị "1" đang lưu không tự đổi).
