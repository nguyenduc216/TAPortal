# Change TINGEE-INTEGRATION-CHG-20261103T151002Z-F31B55

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261103T151002Z-F31B55`
- Created UTC: 2026-11-03T15:10:02.845412+00:00
- Tiêu đề: Tingee Sandbox in Admin: config status, bank link, test QR, payment status and webhook inbox
- Trạng thái: IMPLEMENTED / TESTS_PASS / DEPLOY_PENDING

## Yêu cầu và phạm vi
Operator (2026-11-03): tạo trang Sandbox trên T.A Portal để test tài khoản Tingee của chính T.A trước khi đăng ký Master Merchant: cấu hình, liên kết tài khoản ngân hàng, xuất QR, chuyển tiền thật và nhận diện tiền vào.
Quyết định của operator: Tingee **production**, chế độ Direct (Merchant T.A); webhook nhận **qua site Portal** (chưa deploy app TAPortal.Webhooks riêng); operator **tự điền ClientId/SecretKey** vào appsettings của site Portal trên server (không qua chat, không vào Git).

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MOVE `src/TAPortal.Webhooks/{TingeeWebhookEndpoints,SqlTingeeWebhookInbox}.cs` → `src/TAPortal.Integrations/Tingee/` (namespace `TAPortal.Integrations.Tingee`) để cả Webhooks và Admin dùng chung; thêm giới hạn 64 KB theo từng request (Admin không có giới hạn toàn cục).
- MODIFY `src/TAPortal.Integrations/TAPortal.Integrations.csproj` — Microsoft.Data.SqlClient 7.0.2 + pin Pkcs 8.0.2.
- MODIFY `src/TAPortal.Integrations/Tingee/TingeeOptions.cs` — thêm `VaStatusWebhookUrl`.
- MODIFY `src/TAPortal.Webhooks/Program.cs` — bỏ using namespace cũ.
- MODIFY `src/TAPortal.Web/Program.cs` — đăng ký SqlTingeeWebhookInbox (khi có chuỗi kết nối), ITingeeSandboxInbox, HttpClient; map `/hooks/tingee/{payment,va-status,bank-link}` (ẩn danh, xác thực bằng chữ ký HMAC Tingee).
- ADD `src/TAPortal.Web/Controllers/TingeeSandboxController.cs` (SystemAdminOnly): cấu hình, kiểm tra kết nối (get-va-paging), liên kết NH + OTP, QR động (theo dõi trạng thái mỗi 5 giây) / QR tĩnh, hủy QR, danh sách webhook, gửi webhook tự kiểm tra (bank-link, ký bằng SecretKey của site).
- ADD `src/TAPortal.Web/Models/TingeeSandboxModels.cs`, `Services/TingeeSandboxInbox.cs`, `Views/TingeeSandbox/{Index,_Webhooks}.cshtml`, `wwwroot/js/ta-tingee-sandbox.js`.
- MODIFY `src/TAPortal.Web/Views/Shared/_Layout.cshtml` — menu "Tingee Sandbox" (SYS_ADMIN).
- ADD `tests/TACustomerPortal.Tests/SafeAdminTingeeSandboxTests.cs` (11 test); MODIFY `SafeAdminTingeeTests.cs` (namespace mới của DedupKey).

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path:
- Thứ tự chạy:
- Preflight / Verify / Rollback:
- Execution: NOT_APPLICABLE (dùng bảng dbo.TingeeWebhookInbox của migration 011; không có migration mới). Tài khoản SQL của site Portal cần INSERT/SELECT trên bảng này — kiểm bằng nút "Gửi webhook thử".

## Test và kết quả
- Build Release 0 lỗi; Safe tests 319/319 (11 test mới: chưa cấu hình, kết nối OK/lỗi, từ chối chế độ Master, QR động + trạng thái, QR tĩnh + dữ liệu sai, liên kết NH + OTP, chặn non-admin, webhook ký/không ký trên site Admin, 503 khi chưa cấu hình, parser).

## Publish
- Project:
- Destination (chỉ từ config):
- Trạng thái: NOT_PUBLISHED
- Artifact SHA256:
- Smoke test:

## Git
- Branch:
- Commit SHA:
- PR:
- Merge: PENDING_APPROVAL
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Webhook đặt tạm trên site Portal (operator chọn) thay vì app Webhooks riêng (quyết định kiến trúc #1); khi deploy TAPortal.Webhooks thì chuyển địa chỉ webhook trên Tingee sang đó.
- Hộp thư webhook chỉ lưu, chưa có worker xử lý (cộng tiền đơn hàng); Sandbox chỉ hiển thị.
- "Gửi webhook thử" dùng Host của request làm địa chỉ; chỉ SYS_ADMIN dùng được và nội dung là bản ghi bank-link vô hại.
- Chưa kiểm chứng với Tingee thật: cần operator điền key (production, Mode=Direct) và khai báo địa chỉ webhook trên Tingee.
