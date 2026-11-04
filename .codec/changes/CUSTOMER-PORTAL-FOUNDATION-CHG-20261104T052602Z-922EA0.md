# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261104T052602Z-922EA0

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261104T052602Z-922EA0`
- Created UTC: 2026-11-04T05:26:02.611061+00:00
- Tiêu đề: Admin resets a Customer Portal password; customer must change it at next sign-in
- Trạng thái: DEPLOYED (portal, invoice)

## Yêu cầu và phạm vi
Operator (2026-11-04): tài khoản Customer Portal (vd `tuvanta` của KH TVGPTA) không đăng nhập được, không biết lấy mật khẩu ở đâu và chưa có khôi phục mật khẩu. Hiện trạng: mật khẩu chỉ được đặt khi admin "Tạo tài khoản mới"; không có đặt lại, không có quên mật khẩu, chưa có SMTP.
Quyết định của operator: làm trước "Admin đặt lại mật khẩu"; "Quên mật khẩu qua email" làm sau khi có SMTP.
- Admin, KH → tab "Tài khoản Portal": nút "Đặt lại mật khẩu" từng tài khoản (SYS_ADMIN; chỉ thành viên của KH này; không áp dụng tài khoản nhân viên TA — kiểm cả ở controller và trong SQL). Hệ thống sinh mật khẩu tạm 12 ký tự đạt chính sách (không có ký tự dễ nhầm), lưu hash PBKDF2, đặt `MustChangePassword=1`, ghi audit `CUSTOMER_PORTAL_PASSWORD_RESET` cùng transaction (không ghi mật khẩu). Mật khẩu tạm hiện một lần cho admin (TempData), có nút chép.
- Customer Portal: đăng nhập bằng mật khẩu tạm → chuyển thẳng tới "Đổi mật khẩu" (có thông báo); mọi trang khác bị chuyển về đó cho tới khi đổi; đổi xong cờ về 0 và đăng nhập lại như thường. Phiên cũ của tài khoản bị kết thúc do password stamp đổi.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `database/migrations/015-users-must-change-password.sql`, `database/scripts/019-verify-users-must-change-password.sql`, `database/scripts/020-rollback-users-must-change-password.sql`.
- MODIFY `src/TAPortal.Shared/{PasswordService,MembershipAdminStore}.cs` (GenerateTemporary, ResetPassword).
- MODIFY `src/TAPortal.Web/{Controllers/PortalControllers.cs, Services/CustomerPortalAccounts.cs, Views/Customers/PortalAccounts.cshtml}`.
- MODIFY `src/TACustomerPortal.Web/{Controllers/AccountController.cs, Security/CustomerSecurity.cs, Services/CustomerStore.cs, Program.cs, Views/Account/ChangePassword.cshtml}`.
- MODIFY tests `SafeCustomerWorkspaceTests.cs` (+4), `SafePortalTests.cs` (+1).

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path: `database/migrations/015-users-must-change-password.sql` (thêm 1 cột bit NOT NULL DEFAULT 0; không đổi dữ liệu)
- Thứ tự chạy: sau 014 (013 thuộc CUSTOMER-PROFILE, chưa commit, độc lập). Phải chạy TRƯỚC khi deploy (Customer đọc cột này).
- Preflight / Verify / Rollback: `database/scripts/019-verify-users-must-change-password.sql` / `database/scripts/020-rollback-users-must-change-password.sql`
- Execution: EXECUTED (AGENT MIGRATION EXECUTION)
  - Backup: full backup mới nhất 2026-11-03 21:32 (server), 15 giờ (≤24h).
  - Pre-check 2026-11-04T05:29:32Z: cột chưa có (VERIFY FAIL = chưa áp dụng).
  - Dry-run 2026-11-04T05:29:33Z: migration OK → verify PASS → mọi user hiện có MustChangePassword=0 → rollback 020 OK → verify FAIL (đã gỡ) → ROLLBACK, @@TRANCOUNT=0; verify sau dry-run vẫn FAIL.
  - Gates trên HEAD (checkout sạch): build Release 0 lỗi, Safe tests PASS.
  - Real run: 2026-11-04T05:31:37Z: `dbo.Users.MustChangePassword added`, verify PASS (bit NOT NULL, DF_Users_MustChangePassword). Smoke sau deploy: đăng nhập Customer với user không tồn tại trả thông báo sai tài khoản (HTTP 200), không lỗi dữ liệu.

## Test và kết quả
- Build Release 0 lỗi; Safe tests 351/351 (working tree).

## Publish
- Trạng thái: DEPLOYED qua `deploy/windows/Deploy-TARemote.ps1 -MigrationsApplied` (audit PASS), source b31ba9f.
- portal: artifact 6706d71b8fd7876cd53c4845f81bbed64822b91a1941a89b387991d53851f68f, package 4A887BA45C631C1B991E7ABDA9912F5CF6F448D6B6518194E6917824F7C25F7D, backup C:\TADeploy\backups\portal-20261104-123320, health 200.
- invoice: artifact ee1ed60f0486fe708da0418908866e31c2d5738d5a31318714c4c673db58296d, package 484B081CD14C8DCCEAD51445388FC72B02FBA2C35CD3C3371BF8D57B474A1309, backup C:\TADeploy\backups\invoice-20261104-123335, health 200.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: b31ba9f (source)
- PR:
- Merge: PENDING_APPROVAL
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Admin biết mật khẩu tạm cho tới khi khách đổi (bắt buộc ở lần đăng nhập đầu).
- Chưa có "Quên mật khẩu" tự phục vụ: cần SMTP (operator cung cấp) + bảng token (migration riêng).
- Tài khoản nhân viên TA (màn hình Người dùng) vẫn chưa có đặt lại mật khẩu.
