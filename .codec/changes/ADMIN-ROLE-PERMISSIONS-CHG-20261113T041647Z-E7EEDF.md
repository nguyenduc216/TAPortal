# Change ADMIN-ROLE-PERMISSIONS-CHG-20261113T041647Z-E7EEDF

- Feature ID: `ADMIN-ROLE-PERMISSIONS`
- Change ID: `ADMIN-ROLE-PERMISSIONS-CHG-20261113T041647Z-E7EEDF`
- Created UTC: 2026-11-13T04:16:47.915368+00:00
- Tiêu đề: Mat khau yeu, mat khau khoi tao va bat buoc doi mat khau (portal + hoa don); xoa tai khoan va viec thu
- Trạng thái: DEPLOYED

## Yêu cầu và phạm vi
Operator (2026-11-13):
- Cấp tài khoản nhân viên với mật khẩu 123456; tài khoản dùng mật khẩu yếu phải đổi mật khẩu mới ngay sau khi đăng nhập; mật khẩu mới được gửi về email.
- Danh sách mật khẩu yếu (cách nhau bằng dấu ;) cài đặt trong portal quản trị, áp dụng cho cả portal và hóa đơn; tạo sẵn danh sách mặc định.
- Xóa các tài khoản cũ (trừ admin) và các việc đang có. Operator xác nhận đúng danh sách: 3 tài khoản thử TA_DEV_CRM_019_U1/U2/U3, 3 hồ sơ nhân viên của chúng, 1 việc; giữ admin và 2 tài khoản khách hàng; xóa mềm.

Đã làm:
- `TAPortal.Shared/PasswordSecurity.cs`: `PasswordSecuritySettings` (danh sách mặc định 20 mật khẩu, mật khẩu khởi tạo 123456, so khớp không phân biệt hoa thường, `ValidateNew`, `ValidateAssigned`); `SqlPasswordSecurityStore` đọc/ghi `dbo.Settings` (SYSTEM, key `Security.WeakPasswords`, `Security.TemporaryPassword`), cache 30 giây, lỗi đọc thì dùng mặc định, ghi kèm audit (chỉ số lượng). `PasswordService.HashTemporary` cho mật khẩu khởi tạo.
- Portal quản trị: trang "Bảo mật mật khẩu" (SYS_ADMIN) trong menu Quản lý hệ thống; đăng nhập bằng mật khẩu yếu hoặc `MustChangePassword=1` thì chỉ được vào "Đổi mật khẩu" (cổng mới trong `Program.cs`); đổi xong đặt `MustChangePassword=0`, gửi mật khẩu mới qua kênh email SYSTEM, đăng xuất để đăng nhập lại. Tạo tài khoản nhân viên có ô "Dùng mật khẩu khởi tạo mặc định"; mật khẩu yếu/khởi tạo do quản trị đặt thì tài khoản được lưu `MustChangePassword=1`.
- Hóa đơn: đăng nhập bằng mật khẩu yếu bật `MustChangePassword` (cổng đã có); đổi/đặt lại mật khẩu không được dùng mật khẩu yếu; đổi xong gửi mật khẩu mới qua email (chỉ nói "Hóa đơn tự động").
- Migration 037: xóa mềm 3 tài khoản thử, 3 hồ sơ nhân viên liên kết, việc còn lại và checklist của nó (đánh dấu DeletedAt 2026-11-13 00:37:00 để rollback đúng các dòng này).

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `src/TAPortal.Shared/PasswordSecurity.cs`, `src/TAPortal.Web/Controllers/PasswordSecurityController.cs`, `src/TAPortal.Web/Views/PasswordSecurity/Index.cshtml`, `tests/TACustomerPortal.Tests/SafePasswordSecurityTests.cs`
- ADD `database/migrations/037-remove-test-accounts-and-tasks.sql`, `database/scripts/063-verify-remove-test-accounts-and-tasks.sql`, `database/scripts/064-rollback-remove-test-accounts-and-tasks.sql`
- MODIFY `src/TAPortal.Shared/PasswordService.cs`, `src/TAPortal.Integrations/Notifications/NotificationService.cs`
- MODIFY `src/TAPortal.Web/{Program.cs,Controllers/AccountController.cs,Controllers/EmployeesController.cs,Controllers/PortalControllers.cs,Services/PortalDb.cs,Services/EmployeePolicy.cs,Services/EmployeeStore.cs}`, views `Account/{Login,ChangePassword}.cshtml`, `Employees/Details.cshtml`, `Shared/_Layout.cshtml`
- MODIFY `src/TACustomerPortal.Web/{Program.cs,Controllers/AccountController.cs,Services/CustomerStore.cs}`

## Migration SQL (KHÔNG tự chạy)
- 037: EXECUTED (pre-check NOT_APPLIED, dry-run có rollback PASS, chạy thật verify PASS). Log: `.codec/reports/ADMIN-ROLE-PERMISSIONS-CHG-20261113T041647Z-E7EEDF-migrations-20261113T041826Z.log`.
- `database/migrations/037-remove-test-accounts-and-tasks.sql` (verify 063, rollback 064), chạy sau 036 bằng `Invoke-TAMigrations.ps1 -Plan "037:063:064"`. Operator đã đồng ý cho migration cập nhật dòng dữ liệu đang có.

## Test và kết quả
- Release build 0 lỗi; Safe tests 677/677 (mới: SafePasswordSecurityTests).

## Publish
- portal, invoice, webhooks (Shared và Integrations thay đổi). Xem report.

## Git
- Branch: feature/tx-sync
- Commit SHA: 3fc1dab
- PR:
- Merge: fast-forward main
- Merge SHA:

## Bổ sung 2026-11-13: quy tắc tên đăng nhập (bắt buộc)
Operator: "tên đăng nhập là không có dấu, Unicode không có khoảng cách, chỉ chấp nhận dấu chấm, dấu gạch dưới"; 6 tài khoản nhân viên đặt theo tên + viết tắt họ đệm.
- `TAPortal.Shared/UsernameRules.cs`: chỉ `[A-Za-z0-9._]`, 3-100 ký tự; `FromFullName` (tên + chữ đầu họ đệm, ví dụ Lê Văn An -> anlv), `FromEmail`.
- Portal: kiểm tra ở tạo người dùng, tạo tài khoản nhân viên (gợi ý sẵn tên theo họ tên), tạo tài khoản khách hàng.
- Hóa đơn: tự đăng ký sinh tên đăng nhập từ phần trước '@' của email (thêm số nếu trùng), email "Thông tin đăng nhập" gửi tên này; đăng nhập được bằng tên hoặc email.
- Dữ liệu: migration riêng 904 (verify 905, rollback 906, để ngoài git) đổi tên đăng nhập 6 tài khoản nhân viên; dry-run PASS, gates 677/677, chạy thật VERIFY PASS (2026-11-13 04:47 UTC). Sau đó 0 tài khoản sai quy tắc.
- Safe tests 678/678.

## Rủi ro, quyết định và việc còn lại
- Gửi mật khẩu dạng chữ thường qua email theo yêu cầu operator: ai đọc được hộp thư là có mật khẩu. Email khuyên xóa sau khi ghi nhớ.
- 6 nhân viên mới (NV000001-NV000006, vai trò STAFF, mật khẩu khởi tạo đã băm, MustChangePassword=1) nhập bằng migration riêng 901 (verify 902, rollback 903) qua Invoke-TAMigrations ngày 2026-11-13 04:37 UTC: pre-check NOT_APPLIED, dry-run có rollback PASS, gates 677/677, chạy thật VERIFY PASS. Ba file SQL chứa thông tin cá nhân nên để ngoài git tại `D:\Working\T.A\Tech\Projects\TAPortal\private-sql`. Kiểm tra sau chạy: 6 tài khoản STAFF, 7 hồ sơ nhân viên đều có tài khoản liên kết, 0 việc.
