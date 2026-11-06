# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T235029Z-86E87C

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T235029Z-86E87C`
- Created UTC: 2026-11-06T23:50:29.958496+00:00
- Tiêu đề: E-invoice: create a draft (status DRAFT) when MISA cannot sign, instead of failing
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator (2026-11-07): với khách hàng dùng MISA trên Customer Portal, nếu chưa ký được hóa đơn vì thiếu thông tin chữ ký số thì không báo lỗi mà tự động tạo hóa đơn nháp, trạng thái "Đã xuất nháp".

Bối cảnh: phát hành qua API (Tingee create-and-issue → MISA) trả 400 "CallSignServiceFail" khi doanh nghiệp chưa có chữ ký số dùng được cho phát hành tự động (MISA eSign ký phiên không áp dụng; tài liệu API MISA nêu HSM). Tạo nháp (create) vẫn chạy.

Sửa:
- `OrderInvoiceStatus.Draft = "DRAFT"`; `OrderInvoiceRules.IsSigningFailure` (thông điệp chứa "SignService") và `DraftNote`.
- `OrderInvoicer.IssueAsync`: lỗi ký ⇒ gọi `CreateDraftAsync` với cùng request (cùng invoiceCode cố định của đơn) ⇒ `store.DraftAsync` (DRAFT, ghi chú hướng dẫn), không gửi email, không tự thử lại. Nháp đã có (2034 khi tạo nháp) cũng ⇒ DRAFT. Tạo nháp lỗi khác ⇒ xử lý như trước (thử lại/FAILED với lý do). Xuất lại từ DRAFT: phát hành được ⇒ ISSUED; 2034 khi đơn đang DRAFT (mã do chính bản nháp giữ) ⇒ vẫn DRAFT, không bao giờ báo đã phát hành.
- `SqlOrderInvoiceStore`: ClaimAsync cho xuất tay đơn DRAFT (đọc FromDraft), thêm DraftAsync.
- Customer Portal: badge "Đã xuất nháp" (xanh dương) ở danh sách và chi tiết đơn; ghi chú hiện dạng thông tin (không đỏ); thông báo sau khi bấm xuất là thông báo thường (không lỗi); nút "Xem bản nháp PDF" và "Xuất lại hóa đơn"; trang /pay coi DRAFT như đang xuất.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `database/migrations/026-order-invoice-draft.sql`, `database/scripts/041-verify-order-invoice-draft.sql`, `database/scripts/042-rollback-order-invoice-draft.sql`
- MODIFY `src/TAPortal.Integrations/Tingee/OrderInvoices.cs`, `SqlOrderInvoiceStore.cs`
- MODIFY `src/TACustomerPortal.Web/Controllers/OrdersController.cs`, `Models/EInvoiceModels.cs`, `Views/Orders/Details.cshtml`, `Views/Orders/Index.cshtml`, `Views/Pay/Index.cshtml`
- MODIFY `tests/TACustomerPortal.Tests/SafeCustomerEInvoiceTests.cs`

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- `026-order-invoice-draft.sql`: CK_CO_InvoiceStatus thêm 'DRAFT' (chỉ nới CHECK, không đổi dòng nào). Verify 041, rollback 042 (từ chối nếu đã có đơn DRAFT).
- Execution: xem report

## Test và kết quả
- Release build 0 lỗi; Safe tests 485/485 (mới: WhenSigningFailsADraftIsCreatedInsteadOfAnError, ADraftCanBeIssuedAgainByHandOnceSigningWorks, OnlySigningFailuresBecomeDraftsAndAFailedDraftIsReported, ADraftOrderShowsItsStatusWithoutAnError).

## Publish
- Integrations thay đổi ⇒ webhooks (worker tự xuất), invoice (Customer Portal), portal. Xem report.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: xem report
- PR:
- Merge: xem report
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Chưa xác minh với Tingee/MISA: lỗi ký xảy ra sau khi MISA đã nhận hóa đơn hay chưa; nếu tạo nháp bị từ chối vì trùng mã (2034) thì coi như nháp đã có.
- Đơn DRAFT không tự thử lại; doanh nghiệp ký và phát hành bản nháp trên MISA, hoặc bấm "Xuất lại hóa đơn" sau khi có chữ ký số dùng được. Nếu doanh nghiệp đã phát hành bản nháp trên MISA, T.A chưa tự biết (vẫn hiện DRAFT).
- Đơn DH261106-0001 hiện FAILED: không tự xuất lại thay operator; operator bấm "Xuất lại hóa đơn" để tạo nháp.
