# Change USER-IMPERSONATION-CHG-20261114T011414Z-7AE972

- Feature ID: `USER-IMPERSONATION`
- Change ID: `USER-IMPERSONATION-CHG-20261114T011414Z-7AE972`
- Created UTC: 2026-11-14T01:14:14.357719+00:00
- Tiêu đề: Truy cap ho - dang nhap duoi quyen tai khoan nhan vien, ghi nhan nguoi thao tac
- Trạng thái: DEPLOYED

## Yêu cầu và phạm vi
Operator (2026-11-14): trên TAPortal thêm "truy cập hộ" (đăng nhập dưới quyền tài khoản nhân viên): bấm nút, nhập lý do, rồi làm việc dưới quyền tài khoản đó. Hệ thống ghi lại lịch sử và mọi thao tác, ghi nhận là "<người thao tác> đăng nhập dưới quyền <tài khoản>". Tên chuẩn: User Impersonation ("Login as").

Đã làm:
- Quyền mới `AUTH.USERS.IMPERSONATE` (Hệ thống; SYS_ADMIN có sẵn, role khác cấp ở Vai trò & phân quyền).
- Người dùng > ⋮ > "Truy cập hộ" -> trang xác nhận, lý do 10-500 ký tự -> cookie chuyển sang tài khoản đó (đúng quyền của tài khoản đó) kèm claim người thao tác, phiên, lý do, hạn 60 phút.
- Không được: tự truy cập hộ mình, tài khoản SYS_ADMIN, tài khoản khóa/không có quyền, truy cập hộ lồng nhau; trong phiên không mở đổi mật khẩu, Zalo phone; cổng xác minh số điện thoại không áp dụng.
- Thanh màu cam trên mọi trang: ai đang truy cập hộ ai, lý do, giờ hết hạn, nút "Thoát truy cập hộ" (AccountController.StopImpersonation, đăng nhập lại tài khoản của mình). Đăng xuất cũng kết thúc phiên.
- Mỗi request kiểm tra lại: phiên còn mở, chưa hết hạn, người thao tác còn hợp lệ và còn quyền, tài khoản dùng còn hợp lệ; nếu không -> kết thúc (TIMEOUT/REVOKED) và trả về tài khoản người thao tác (hoặc đăng xuất).
- Ghi nhận: dbo.ImpersonationSessions (ai, tài khoản nào, lý do, giờ, IP, trình duyệt, cách kết thúc); dbo.ImpersonationActions (mọi request: method, path, status); dbo.AuditLogs thêm ImpersonatorUserId/ImpersonationSessionId cho mọi thay đổi có audit (ImpersonationAmbient AsyncLocal, AuditSql); dbo.WorkTaskActivities thêm ImpersonatorUserId, lịch sử công việc hiện "<người thao tác> (truy cập hộ <tài khoản>)".
- Quản lý hệ thống > "Lịch sử truy cập hộ": danh sách phiên và chi tiết (thay đổi dữ liệu + các trang đã mở).
- Shell: không prefetch khi rê chuột trong phiên truy cập hộ (lịch sử chỉ có trang thực sự mở); đổi tài khoản ở tab khác -> tải lại cả trang (data-ta-identity). ta-shell.js giống nhau ở 2 app.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `database/migrations/042-user-impersonation.sql`, `database/scripts/073-verify-user-impersonation.sql`, `database/scripts/074-rollback-user-impersonation.sql`
- ADD `src/TAPortal.Web/Security/Impersonation.cs`, `src/TAPortal.Web/Services/ImpersonationStore.cs`, `src/TAPortal.Web/Controllers/ImpersonationController.cs`, `src/TAPortal.Web/Controllers/AccountImpersonationActions.cs`, `src/TAPortal.Web/Views/Impersonation/{Start,Index,Details}.cshtml`, `tests/TACustomerPortal.Tests/SafeImpersonationTests.cs`
- MODIFY `src/TAPortal.Shared/AuditTrail.cs`, `src/TAPortal.Web/Program.cs`, `Controllers/AccountController.cs`, `Security/AdminPermissions.cs`, `Services/WorkTaskStore.cs`, `Services/WorkTaskPolicy.cs`, `Views/Shared/_Layout.cshtml`, `Views/Users/Index.cshtml`, `wwwroot/css/ta-admin.css`, `wwwroot/js/ta-shell.js` (2 app)

## Migration SQL
- Database: TAPortalSys
- Script: `database/migrations/042-user-impersonation.sql` (sau 041)
- Verify 073, Rollback 074.
- Execution: EXECUTED (Invoke-TAMigrations 042:073:074).

## Test và kết quả
- Safe tests 735/735 (thêm SafeImpersonationTests 11).

## Publish
- portal, invoice, webhooks (Shared đổi). Xem report.

## Git
- Branch: feature/tx-sync
- Commit SHA: aaa87e6
- Merge: main

## Rủi ro, quyết định và việc còn lại
- Chưa thử truy cập hộ thật trên production (không đăng nhập được).
- Bảng lịch sử riêng khác (EmployeeStatusHistories, ...) vẫn ghi tài khoản dùng; người thao tác thật nằm ở AuditLogs và ImpersonationActions.
