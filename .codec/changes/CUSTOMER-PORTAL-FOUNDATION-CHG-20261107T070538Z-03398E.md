# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261107T070538Z-03398E

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261107T070538Z-03398E`
- Created UTC: 2026-11-07T07:05:38.262144+00:00
- Tiêu đề: Xung ho Hoa don tu dong tren Customer Portal, dong San pham cua T.A cuoi sidebar
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator (2026-11-07): "Bỏ hết các nội dung liên quan tới T.A trên trang hoá đơn tự động, toàn bộ xưng hô đều là theo hoá đơn tự động. chỉ thêm 1 hàng nhỏ xíu ở phía dưới cùng của thanh sidebar "Sản phẩm của Tư vấn giải pháp T.A" và link tới trang tuvangiaiphapta.vn".

Sửa: thương hiệu sidebar / trang đăng nhập / tiêu đề trang thành "Hóa đơn tự động" (logo hiện có, dòng phụ "Thu tiền & xuất hóa đơn"); mọi thông báo, lỗi, email đặt lại mật khẩu, meta trang thanh toán dùng "Hóa đơn tự động" hoặc "bộ phận hỗ trợ Hóa đơn tự động" thay cho "T.A" / "Customer Portal"; dòng nhỏ `.cp-maker` cuối sidebar dẫn tới https://tuvangiaiphapta.vn (ẩn khi thu gọn sidebar). Quy tắc mới ghi vào AGENTS.md (CUSTOMER PORTAL BRAND) và test SafeCustomerBrandTests.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TACustomerPortal.Web/Views/**` (Shared/_Layout, Shared/_PaymentsNotLinked, Shared/PaymentsUnavailable, Account/*, Banks/*, Dashboard, EInvoice, Notifications, Pay, Profile/Company, ServicePackages), `Controllers/AccountController.cs`, `BanksController.cs`, `OrdersController.cs`, `EInvoiceController.cs`, `NotificationsController.cs`, `ServicePackagesController.cs`, `Services/OrderPayments.cs`, `Services/PasswordRecovery.cs`, `appsettings.json`, `wwwroot/css/customer.css`
- MODIFY `AGENTS.md`; ADD `tests/TACustomerPortal.Tests/SafeCustomerBrandTests.cs`; MODIFY `SafeCustomerPaymentsTests.cs`

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Không có migration.
- Execution: NOT_APPLICABLE

## Test và kết quả
- Release build 0 lỗi; Safe tests 594/594.

## Publish
- invoice (TACustomerPortal.Web). Xem report.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: xem report
- PR:
- Merge: xem report
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Tên người gửi email trên production lấy từ appsettings của server (deploy không ghi đè); nếu đang là "T.A Customer Portal" thì operator đổi `PasswordRecovery:FromName` thành "Hóa đơn tự động".
- Dữ liệu khách hàng có chữ T.A (tên doanh nghiệp) không bị ảnh hưởng.
