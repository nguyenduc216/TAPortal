# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T012601Z-147F73

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T012601Z-147F73`
- Created UTC: 2026-11-06T01:26:01.233813+00:00
- Tiêu đề: Customer Portal: MISA e-invoice settings and automatic invoice for paid QR orders
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator (2026-11-06): bổ sung cài đặt hóa đơn điện tử phía khách hàng (giống Hóa đơn Sandbox trên TA Portal), trước hết là cấu hình xuất hóa đơn tự động; khi có cấu hình thì đơn hàng QR thanh toán xong xuất luôn hóa đơn tự động. Chỉ MISA meInvoice.

Trong phạm vi:
- Customer Portal `/einvoice` (menu Thanh toán → Hóa đơn điện tử; OWNER/ADMIN thay đổi, thành viên khác chỉ xem): (1) kết nối tài khoản MISA meInvoice (MST người bán, tên đăng nhập, mật khẩu) qua API e-invoice của đối tác bằng merchant của doanh nghiệp — mật khẩu chỉ chuyển một lần cho đối tác, T.A không lưu; đổi tài khoản / gỡ kết nối; (2) lấy mẫu hóa đơn (mẫu số + ký hiệu) từ MISA hoặc nhập tay; (3) bật "tự động xuất hóa đơn khi đơn QR thanh toán đủ", gửi email cho người mua; (4) xem thử hóa đơn nháp (PDF, không gửi thuế).
- Xuất tự động: `OrderInvoicer` + `OrderInvoiceWorker` (site webhook, 15 giây/lần) xuất hóa đơn cho đơn PAID có AutoIssueInvoice=1 của doanh nghiệp đã bật tự động và đã chọn mẫu, chỉ đơn thanh toán đủ từ lúc bật (AutoIssueFrom). Mã hóa đơn cố định theo đơn (số đơn + 8 ký tự đầu Id) nên thử lại không bao giờ xuất 2 hóa đơn (2034 ⇒ coi là đã xuất). Lỗi tạm thời: thử lại sau 1, 2, 4… phút, tối đa 6 lần rồi FAILED; lỗi cấu hình (2032, 2037, 1057, 2038, 2007…) ⇒ FAILED ngay với lý do.
- Nội dung hóa đơn: doanh nghiệp/tổ chức ⇒ tên đơn vị + MST; khách lẻ ⇒ tên; hộ kinh doanh có MST ⇒ tên + MST; đơn không chọn khách ⇒ "Người mua không lấy hóa đơn". Hình thức thanh toán chuyển khoản, ngày hóa đơn = ngày xuất (giờ VN), dòng hàng theo đơn.
- Trang chi tiết đơn: trạng thái hóa đơn, số hóa đơn, lỗi (đã lọc tên đối tác), nút "Xuất hóa đơn"/"Xuất lại hóa đơn" (đơn đã thanh toán đủ, kể cả đơn trước khi bật tự động), "Xem hóa đơn PDF".
- Cài đặt thanh toán: ghi chú trỏ tới trang Hóa đơn điện tử.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `database/migrations/022-customer-einvoice.sql`, `database/scripts/033-verify-customer-einvoice.sql`, `database/scripts/034-rollback-customer-einvoice.sql`
- ADD `src/TAPortal.Integrations/Tingee/OrderInvoices.cs`, `src/TAPortal.Integrations/Tingee/SqlOrderInvoiceStore.cs`
- MODIFY `src/TAPortal.Webhooks/Program.cs` (đăng ký store, invoicer, worker)
- ADD `src/TACustomerPortal.Web/Controllers/EInvoiceController.cs`, `Models/EInvoiceModels.cs`, `Views/EInvoice/Index.cshtml`
- MODIFY `src/TACustomerPortal.Web/Controllers/OrdersController.cs`, `Models/PaymentModels.cs`, `Services/PaymentStore.cs`, `Program.cs`, `Views/Orders/Details.cshtml`, `Views/PaymentSettings/Index.cshtml`, `Views/Shared/_Layout.cshtml`, `wwwroot/css/customer.css`
- ADD `tests/TACustomerPortal.Tests/SafeCustomerEInvoiceTests.cs`; MODIFY `SafeCustomerPaymentsTests.cs` (factory nhận invoice store/API giả)

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- 022-customer-einvoice.sql: tạo dbo.CustomerInvoiceSettings (không có cột mật khẩu; CK_CIS_Provider = 'misa'; FK Customers), thêm 10 cột hóa đơn vào dbo.CustomerOrders (InvoiceCode, InvoiceNumber, InvoiceLookupCode, InvoiceTrackingCode, InvoiceIssuedAt, InvoiceEmailedAt, InvoiceError, InvoiceAttempts default 0, InvoiceNextAttemptAt, InvoiceLockedUntil), UX_CO_InvoiceCode (unique, filtered), IX_CO_InvoiceQueue. Không sửa dòng hiện có. Verify 033, rollback 034 (từ chối khi đã có cài đặt hoặc mã hóa đơn).
- Execution: xem report.

## Test và kết quả
- `dotnet build TAPortal.sln -c Release`: 0 lỗi. Safe tests: 467/467 pass. `test_codec.py` OK. `git diff --check` OK.
- SafeCustomerEInvoiceTests: mã hóa đơn cố định; nội dung theo loại người mua; xuất một lần + email; chỉ đơn thanh toán sau khi bật; mất phản hồi không xuất trùng (2034); lỗi từ chối ⇒ FAILED rồi xuất lại tay; giới hạn thử lại; không xuất khi thiếu mẫu; kết nối MISA không lưu mật khẩu; bật tự động cần tài khoản + mẫu; thành viên không sửa được; xuất từ trang đơn và mở PDF.

## Publish
- invoice (TACustomerPortal.Web) và webhooks (TAPortal.Webhooks). Portal Admin không đổi. Xem report.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: 71cb50a
- PR:
- Merge: xem report
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Chưa thử với tài khoản MISA thật: cần doanh nghiệp kết nối tài khoản MISA và một đơn QR thật để xác nhận (mẫu số/ký hiệu, email).
- Hóa đơn xuất tự động chưa trừ hạn mức hóa đơn của gói dịch vụ (InvoicesRemaining) — change sau nếu operator muốn chặn khi hết gói.
- Đơn có tiền dư (trả dư) vẫn xuất theo giá trị đơn; đơn EXPIRED_PAID/CANCELLED_PAID không xuất tự động (chờ đối soát).
