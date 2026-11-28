# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261128T050250Z-AC5601

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261128T050250Z-AC5601`
- Created UTC: 2026-11-28T05:02:50.547277+00:00
- Tiêu đề: E-invoice: demo and production connections side by side, template list picks the default, Add template button
- Trạng thái: PLANNED

## Yêu cầu và phạm vi
Operator (2026-11-28), trang Hóa đơn điện tử:
- Danh sách mẫu chỉ để chọn mặc định; thêm mẫu mới bằng nút riêng. Hiện tại chọn "Thêm mẫu mới" vẫn giữ mẫu số và ký hiệu cũ, và mục được chọn không được tô sáng.
- Tách môi trường demo và production của SoftDreams, chuyển qua lại được.

Quyết định:
- Danh sách mẫu: radio chọn mặc định, mục đang chọn được tô sáng. Nút "Thêm mẫu" mở hộp thoại (mẫu số, ký hiệu, tùy chọn "Dùng làm mẫu mặc định") gọi POST AddTemplate. Khi đã có danh sách thì ẩn ô nhập tay; ô nhập tay chỉ còn cho mẫu đầu tiên và cho MISA/Viettel.
- Môi trường: bảng mới dbo.CustomerInvoiceConnections (migration 071) giữ đăng nhập (mật khẩu mã hóa) theo nhà cung cấp và môi trường. Kết nối đang dùng vẫn nằm ở CustomerInvoiceSettings nên bộ xuất hóa đơn không đổi.
  - Chuyển môi trường (POST SwitchEnvironment): lưu kết nối đang dùng, nạp kết nối và mẫu mặc định của môi trường kia. Có hộp xác nhận nói rõ hóa đơn thật hay thử.
  - Kết nối môi trường mới (Index?env=LIVE|DEMO) giữ lại môi trường cũ.
- CustomerInvoiceTemplates.IsDefault: mẫu mặc định riêng từng môi trường.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `database/migrations/071-einvoice-environments.sql`, `database/scripts/129-verify-einvoice-environments.sql`, `database/scripts/130-rollback-einvoice-environments.sql`
- MODIFY `src/TAPortal.Integrations/Tingee/OrderInvoices.cs` (InvoiceConnection, IsDefault, phương thức store), `SqlOrderInvoiceStore.cs`
- MODIFY `src/TACustomerPortal.Web/Controllers/EInvoiceController.cs` (Index env, AddTemplate, SwitchEnvironment, KeepActiveConnection), `Models/EInvoiceModels.cs`, `Views/EInvoice/Index.cshtml`, `wwwroot/css/customer.css`, `wwwroot/js/customer.js`
- MODIFY `tests/TACustomerPortal.Tests/SafeCustomerEInvoiceTests.cs`, `SafeEasyInvoiceTests.cs`

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script: `database/migrations/071-einvoice-environments.sql`
- Thứ tự chạy: sau 070
- Preflight / Verify / Rollback: `129-verify-einvoice-environments.sql` / `130-rollback-einvoice-environments.sql`. Thêm 1 bảng và 1 cột có default, không đổi dòng nào.
- Execution: NOT_EXECUTED

## Test và kết quả
- Build 0 lỗi. Safe tests 873 PASS (test mới DemoAndProductionAreKeptSideBySideAndSwitched; cập nhật test danh sách mẫu).

## Publish
- Project: TACustomerPortal.Web (Integrations đổi; portal/webhooks không dùng phần mới)
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
- Hóa đơn đã xuất ở môi trường nào chỉ tải PDF được khi đang dùng môi trường đó (đơn chưa lưu môi trường đã xuất).
- Kết nối demo đang dùng trên production chưa có dòng trong bảng mới; trang tự hiện nó là "Đang dùng" và lần chuyển đầu tiên lưu nó lại.
