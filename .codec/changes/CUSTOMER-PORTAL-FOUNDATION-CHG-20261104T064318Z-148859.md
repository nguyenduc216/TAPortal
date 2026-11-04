# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261104T064318Z-148859

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261104T064318Z-148859`
- Created UTC: 2026-11-04T06:43:18.069459+00:00
- Tiêu đề: Customer Portal forgot-password by email (SMTP, one-time 30-minute link)
- Trạng thái: IMPLEMENTED / TESTS_PASS

## Yêu cầu và phạm vi
Operator (2026-11-04): làm "Quên mật khẩu" qua email, địa chỉ gửi `noreply@hoadontudong.vn`.
Kiểm tra DNS 2026-11-04: hoadontudong.vn (NS zonedns.vn) chỉ có bản ghi A → 113.160.249.61; CHƯA có MX/SPF/DKIM/DMARC, tức chưa có dịch vụ email → operator cần đăng ký dịch vụ email/SMTP cho tên miền và thêm SPF/DKIM trước khi bật.
- Customer Portal: link "Quên mật khẩu?" ở trang đăng nhập → `/account/forgot-password` (tên đăng nhập hoặc email) → email chứa liên kết `/account/reset-password?token=…` hiệu lực 30 phút, dùng một lần; yêu cầu mới vô hiệu liên kết cũ; tối đa 3 yêu cầu/giờ/tài khoản; giới hạn tần suất theo IP (policy "login"). Câu trả lời luôn giống nhau (không lộ tài khoản có tồn tại/có email). Chỉ tài khoản thành viên Customer Portal đang hoạt động, có email, không phải nhân viên TA. Đặt mật khẩu mới theo chính sách hiện có, xóa cờ MustChangePassword, phiên cũ kết thúc (password stamp).
- Chỉ lưu SHA-256 của token (`dbo.PasswordResetTokens`, migration 016). Liên kết dựng từ `Email:PublicBaseUrl` (https bắt buộc), không từ Host của request.
- SMTP qua System.Net.Mail (STARTTLS cổng 587; không hỗ trợ SSL ngầm cổng 465). Chưa cấu hình `Email` ⇒ trang báo liên hệ quản trị viên T.A (dùng "Đặt lại mật khẩu" ở Admin).

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `database/migrations/016-password-reset-tokens.sql`, `database/scripts/021-verify-password-reset-tokens.sql`, `database/scripts/022-rollback-password-reset-tokens.sql`.
- ADD `src/TACustomerPortal.Web/Services/PasswordRecovery.cs`, `Views/Account/{ForgotPassword,ResetPassword}.cshtml`.
- MODIFY `src/TACustomerPortal.Web/{Controllers/AccountController.cs, Models/AccountModels.cs, Program.cs, Security/CustomerSecurity.cs, Views/Account/Login.cshtml, appsettings.json (mục Email để trống), wwwroot/css/customer.css}`.
- ADD tests `SafePasswordRecoveryTests.cs` (6); MODIFY `SafeReleaseRegressionTests.cs` (4 endpoint ẩn danh mới đã duyệt).

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path: `database/migrations/016-password-reset-tokens.sql` (bảng mới; không đổi đối tượng/dữ liệu có sẵn)
- Thứ tự chạy: sau 015. Phải chạy TRƯỚC khi deploy.
- Preflight / Verify / Rollback: `database/scripts/021-verify-password-reset-tokens.sql` / `database/scripts/022-rollback-password-reset-tokens.sql`
- Execution: xem `migration_status`.

## Test và kết quả
- Build Release 0 lỗi; Safe tests 357/357 (working tree).

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
- Chưa gửi thử được email thật: hoadontudong.vn chưa có dịch vụ email. Operator: đăng ký email/SMTP cho tên miền, thêm SPF + DKIM (+ DMARC) vào DNS zonedns.vn, rồi điền mục `Email` trong appsettings.json của site invoice (Host, Port 587, Username, Password, FromAddress=noreply@hoadontudong.vn, PublicBaseUrl=https://hoadon.tuvangiaiphapta.vn) và recycle app pool TA.Customer.
- Gửi mail chạy đồng bộ trong request (≤20 giây); lỗi SMTP chỉ ghi log, người dùng vẫn thấy câu trả lời chung.
