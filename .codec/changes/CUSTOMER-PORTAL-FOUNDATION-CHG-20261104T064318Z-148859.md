# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261104T064318Z-148859

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261104T064318Z-148859`
- Created UTC: 2026-11-04T06:43:18.069459+00:00
- Tiêu đề: Customer Portal forgot-password by email (SMTP, one-time 30-minute link)
- Trạng thái: DEPLOYED (portal, invoice) — email chưa cấu hình

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
- Execution: EXECUTED (AGENT MIGRATION EXECUTION)
  - Backup: full backup mới nhất 2026-11-03 21:32 (server), 16 giờ (≤24h).
  - Pre-check 2026-11-04T06:49:35Z: bảng chưa có (VERIFY FAIL = chưa áp dụng).
  - Dry-run 2026-11-04T06:49:36Z: migration OK → verify PASS → rollback 022 OK → verify FAIL (đã gỡ) → ROLLBACK, @@TRANCOUNT=0; verify sau dry-run vẫn FAIL.
  - Gates trên HEAD (checkout sạch): build Release 0 lỗi, Safe tests PASS.
  - Real run: 2026-11-04T06:52:06Z: `dbo.PasswordResetTokens created`, verify PASS (bảng, 7 cột, UX_PRT_TokenHash, FK_PRT_User). Các câu SQL của SqlPasswordResetStore biên dịch được trên schema thật (SET NOEXEC ON, không thực thi). Smoke sau deploy: /account/login có link "Quên mật khẩu?", /account/forgot-password 200 báo chưa bật (chưa cấu hình Email).

## Test và kết quả
- Build Release 0 lỗi; Safe tests 357/357 (working tree).

## Publish
- Trạng thái: DEPLOYED qua `deploy/windows/Deploy-TARemote.ps1 -MigrationsApplied` (audit PASS), source 2764a35.
- portal: artifact 8539d2774ffcc1a690ea235e84a8e5fed50bf6ac54e230680f5bbabe3a15779d, package 01B362F67D7600FEBB147267ACA12D73A9034B3960624773578F0D65908990AF, backup C:\TADeploy\backups\portal-20261104-135517, health 200.
- invoice: artifact 272ad7f949e8650f6aa900ec20aff3af651a40a59f2a37d3f62922dd2c2908f3, package B34CEDF66DD80C6141F34D8F108C2C6B03745B2D2E354798387C895C9A3CE592, backup C:\TADeploy\backups\invoice-20261104-135552, health 200.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: 2764a35 (source)
- PR:
- Merge: PENDING_APPROVAL
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Chưa gửi thử được email thật: hoadontudong.vn chưa có dịch vụ email. Operator: đăng ký email/SMTP cho tên miền, thêm SPF + DKIM (+ DMARC) vào DNS zonedns.vn, rồi điền mục `Email` trong appsettings.json của site invoice (Host, Port 587, Username, Password, FromAddress=noreply@hoadontudong.vn, PublicBaseUrl=https://hoadon.tuvangiaiphapta.vn) và recycle app pool TA.Customer.
- Gửi mail chạy đồng bộ trong request (≤20 giây); lỗi SMTP chỉ ghi log, người dùng vẫn thấy câu trả lời chung.
