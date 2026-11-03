# Change TINGEE-INTEGRATION-CHG-20261103T151002Z-F31B55

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261103T151002Z-F31B55`
- Created UTC: 2026-11-03T15:10:02.845412+00:00
- Tiêu đề: Tingee Sandbox in Admin: config status, bank link, test QR, payment status and webhook inbox
- Trạng thái: DEPLOYED (portal, invoice) / TINGEE_NOT_CONFIGURED

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
- Lần 1: audit FAIL ("filled secret value" — dòng mẫu SecretKey trong trang hướng dẫn); không push/deploy. Sửa: mẫu để trống (ea49b83).
- portal → TA.Portal: DEPLOYED 2026-11-03 15:24 UTC, source ea49b83, artifact 480c8a95eaca42c3fcb1fe92b2c53439854f58bcec32ddd4e268ba47d953aad6, package 1F38E986320F0FA4682822EAABBD79EBCF87EB34C7198C18A24360C3655396A7, health 200.
- invoice → TA.Invoice (redeploy vì TAPortal.Integrations đổi): DEPLOYED 2026-11-03 15:24 UTC, source ea49b83, artifact 2ccdd28932e4595d0936b49887f5f490ef95ca8b0851ee7deac6d38003023292, package 85247026BD38FB8DA6371ECC99D917184B99CCE892F0F5F8807C7B9FDC6BC418, health 200.
- Smoke: /tingeesandbox → 302 tới đăng nhập (đúng, chỉ SYS_ADMIN); POST /hooks/tingee/payment không ký → 503 (đúng: Tingee chưa cấu hình trên server).
- Chờ operator: điền key Tingee (production, Mode=Direct) vào appsettings site Portal, khai báo webhook trên Tingee, bấm "Gửi webhook thử".

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
