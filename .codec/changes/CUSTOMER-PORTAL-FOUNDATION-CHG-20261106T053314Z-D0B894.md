# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T053314Z-D0B894

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T053314Z-D0B894`
- Created UTC: 2026-11-06T05:33:14.297075+00:00
- Tiêu đề: Logo doanh nghiệp và khách hàng; trang QR đơn hàng công khai
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator 2026-11-06: logo cho doanh nghiệp dùng hoadontudong và cho từng khách hàng của họ; mỗi đơn hàng có QR có một trang
riêng để khách quét: trên cùng 2 logo (người bán và khách hàng nếu có) kiểu "kết nối", tiêu đề đơn hàng, MST và tên xuất
hóa đơn (sửa được, nút Lưu nhỏ chỉ hiện khi có thay đổi), QR, số tiền, mã đơn, thời gian còn lại đếm ngược. Trang đơn hàng
trong portal có "Xem link QR" mở trang này.

Quyết định kỹ thuật:
- Logo lưu trong DB (`dbo.CustomerLogos`, varbinary, ≤ 1 MB, PNG/JPG/WEBP nhận diện theo nội dung; SVG bị từ chối) để deploy
  không làm mất ảnh và không cần quyền ghi thư mục trên IIS.
- Trang công khai `/pay/{token}`: token ngẫu nhiên 24 byte (base64url 32 ký tự) lưu ở `CustomerOrders.PublicToken`, tạo lần đầu
  khi mở trang đơn trong portal; id đơn (NEWSEQUENTIALID, đoán được) không mở được trang. AllowAnonymous, rate limit `pay`
  (120/phút/IP), POST hóa đơn dùng `lookup` (20/phút) + antiforgery. Layout riêng, không có điều hướng portal, noindex.
- MST/tên người mua sửa trên trang lưu ở đơn (`InvoiceBuyerTaxCode/Name/UpdatedAt`), KHÔNG ghi vào hồ sơ khách hàng; chỉ sửa được
  khi hóa đơn NONE/FAILED và đơn chưa hủy. `SqlOrderInvoiceStore` (TAPortal.Integrations) ưu tiên các giá trị này khi xuất hóa đơn;
  có MST cho khách lẻ/không có hồ sơ ⇒ xuất dạng doanh nghiệp.
- Đếm ngược theo đồng hồ server (bù lệch đồng hồ thiết bị); hết giờ trang tải lại. Trạng thái thanh toán poll `/pay/{token}/status`.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `database/migrations/024-logos-and-order-pay-page.sql`
- ADD `database/scripts/037-verify-logos-and-order-pay-page.sql`
- ADD `database/scripts/038-rollback-logos-and-order-pay-page.sql`
- ADD `src/TACustomerPortal.Web/Services/OrderPageStore.cs` (ILogoStore, IOrderPageStore, LogoUpload, SqlOrderPageStore)
- ADD `src/TACustomerPortal.Web/Controllers/PayController.cs`
- ADD `src/TACustomerPortal.Web/Views/Pay/Index.cshtml`
- MODIFY `src/TACustomerPortal.Web/Controllers/OrdersController.cs` (link trang thanh toán, thông tin người mua cập nhật)
- MODIFY `src/TACustomerPortal.Web/Controllers/ProfileController.cs` (logo doanh nghiệp: Logo POST, CompanyLogo GET)
- MODIFY `src/TACustomerPortal.Web/Controllers/ContactsController.cs` (logo khách hàng khi tạo/sửa; Logo GET)
- MODIFY `src/TACustomerPortal.Web/Models/PaymentModels.cs` (PayInvoiceInput, PayPageVm, OrderDetailsVm)
- MODIFY `src/TACustomerPortal.Web/Program.cs` (đăng ký store, rate limit `pay`)
- MODIFY `src/TACustomerPortal.Web/Views/Orders/Details.cshtml`, `Views/Profile/Company.cshtml`, `Views/Contacts/Edit.cshtml`
- MODIFY `src/TACustomerPortal.Web/wwwroot/css/customer.css`, `wwwroot/js/customer.js`
- MODIFY `src/TAPortal.Integrations/Tingee/SqlOrderInvoiceStore.cs`
- MODIFY `src/TAPortal.Shared/UiIcon.cs` (external-link, link, photo, upload)
- ADD `tests/TACustomerPortal.Tests/SafeCustomerPayPageTests.cs`; MODIFY `SafeCustomerPaymentsTests.cs`, `SafePortalTests.cs`, `SafeReleaseRegressionTests.cs`

## Migration SQL
- Database: TAPortalSys
- Script: `database/migrations/024-logos-and-order-pay-page.sql` (sau 023)
- Verify: `database/scripts/037-verify-logos-and-order-pay-page.sql`; Rollback: `database/scripts/038-rollback-logos-and-order-pay-page.sql`
- Bảng mới + cột nullable; không đổi dữ liệu cũ. Rollback từ chối khi đã có logo hoặc thông tin hóa đơn người mua nhập.
- Execution: NOT_EXECUTED (xem mục Thực thi bên dưới khi chạy)

## Test và kết quả
- `dotnet restore` OK; `dotnet build TAPortal.sln -c Release --no-restore`: 0 errors (2 cảnh báo cũ, không thuộc change).
- Safe tests: 477/477 PASS (mới: SafeCustomerPayPageTests — nhận diện ảnh/SVG, token, trang công khai 2 bên + QR + đếm ngược,
  sửa MST/tên đến khi hóa đơn xuất, upload logo doanh nghiệp/khách hàng, quyền MEMBER; endpoint ẩn danh đã review).
- Codec unittest OK; `git diff --check` OK.
- Kiểm tra giao diện: render trang qua test host + Playwright (desktop 1280, mobile 390).

## Publish
- Project: TACustomerPortal.Web, TAPortal.Web (Shared đổi), TAPortal.Webhooks (Integrations đổi)
- Trạng thái: NOT_PUBLISHED

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: (xem hand-off)
- Merge: PENDING

## Rủi ro, quyết định và việc còn lại
- Phải chạy migration 024 TRƯỚC khi deploy invoice/webhooks: `SqlOrderInvoiceStore` đọc cột mới.
- Ai có link đều xem được đơn (tên 2 bên, số tiền, hàng hóa) và sửa MST/tên xuất hóa đơn cho đến khi hóa đơn được xuất — đúng yêu cầu
  "khách quét"; link không đoán được. Doanh nghiệp thấy giá trị người mua nhập trên trang đơn.
- Chưa có: logo hiển thị ở danh sách khách hàng / sidebar; quản trị viên T.A (Admin portal) chưa xem/sửa logo.
