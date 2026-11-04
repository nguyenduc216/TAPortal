# Change TINGEE-INTEGRATION-CHG-20261104T185412Z-3684CE

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261104T185412Z-3684CE`
- Tiêu đề: Customer Portal: choose receiving account when issuing an order QR; QR lifetime in days/hours/minutes
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator (2026-11-05): đơn DH261105-0001 nhận tiền vào MB trong khi tài khoản mặc định là VietinBank; cần chọn lại tài khoản; thời hạn QR nên nhập theo ngày → giờ → phút.

- Trang tạo đơn chọn sẵn tài khoản mặc định (đã có); danh sách ghi rõ "· mặc định".
- Trang chi tiết đơn (nháp hoặc hết hạn, còn phải thu): ô chọn tài khoản nhận tiền cạnh nút "Tạo QR"; QR tạo theo tài khoản được chọn và đơn lưu lại tài khoản đó (`UpdateOrderAsync` nay ghi `BankAccountId`). Tài khoản không hoạt động/khác doanh nghiệp bị từ chối.
- Thời hạn QR (Cài đặt thanh toán và Tạo đơn): 3 ô ngày (0–30) / giờ (0–23) / phút (0–59), hiển thị tổng; lưu vẫn bằng phút (1 phút – 30 ngày). Form cũ gửi số phút vẫn được nhận.
- Nguyên nhân đơn dùng MB: chưa xác minh được (đọc DB production bị chặn quyền). Code chọn sẵn tài khoản `IsDefault`; khả năng cao lúc tạo đơn MB đang là mặc định hoặc người tạo chọn MB.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TACustomerPortal.Web/Models/PaymentModels.cs` (QrLifetime, QrLifetimeField, PaymentText.BankOption, OrderDetailsVm.Banks)
- MODIFY `src/TACustomerPortal.Web/Controllers/OrdersController.cs`
- MODIFY `src/TACustomerPortal.Web/Controllers/PaymentSettingsController.cs`
- MODIFY `src/TACustomerPortal.Web/Services/PaymentStore.cs` (UPDATE CustomerOrders ghi BankAccountId)
- ADD `src/TACustomerPortal.Web/Views/Shared/_QrLifetime.cshtml`
- MODIFY `src/TACustomerPortal.Web/Views/Orders/Create.cshtml`, `Views/Orders/Details.cshtml`, `Views/PaymentSettings/Index.cshtml`
- MODIFY `src/TACustomerPortal.Web/wwwroot/css/customer.css`, `wwwroot/js/customer.js`
- MODIFY `tests/TACustomerPortal.Tests/SafeCustomerPaymentsTests.cs` (2 test mới)

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Execution: NOT_APPLICABLE (cột BankAccountId đã có)

## Test và kết quả
- Release build 0 lỗi; Safe tests 398/398 pass.

## Publish
- Project: TACustomerPortal.Web (invoice). Trạng thái: xem report.

## Git
- Branch: feature/customer-portal-mvp01
- Merge: PENDING_APPROVAL

## Rủi ro, quyết định và việc còn lại
- Đổi tài khoản chỉ khi đơn chưa có QR hoặc QR đã hết hạn (đơn đang có QR mở phải hủy/hết hạn trước).