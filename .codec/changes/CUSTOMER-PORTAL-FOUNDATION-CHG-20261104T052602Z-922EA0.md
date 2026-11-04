# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261104T052602Z-922EA0

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261104T052602Z-922EA0`
- Created UTC: 2026-11-04T05:26:02.611061+00:00
- Tiêu đề: Admin resets a Customer Portal password; customer must change it at next sign-in
- Trạng thái: IMPLEMENTED / TESTS_PASS

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
- Execution: xem `migration_status`.

## Test và kết quả
- Build Release 0 lỗi; Safe tests 351/351 (working tree).

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
- Admin biết mật khẩu tạm cho tới khi khách đổi (bắt buộc ở lần đăng nhập đầu).
- Chưa có "Quên mật khẩu" tự phục vụ: cần SMTP (operator cung cấp) + bảng token (migration riêng).
- Tài khoản nhân viên TA (màn hình Người dùng) vẫn chưa có đặt lại mật khẩu.
