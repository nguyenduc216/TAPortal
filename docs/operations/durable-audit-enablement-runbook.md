# Bật audit bền vững cho TAPortal Admin (AUD-25 / AUD-29)

Runbook cho operator/DBA. Không có bước nào do AI agent thực hiện. Database duy nhất: `TAPortalSys` trên `WIN-HSHL54Q184K\MSSQLSERVER2014`.

## Thao tác được audit (Checkpoint 9)
| Action | Màn hình | Ghi trong cùng transaction |
|---|---|---|
| USER_CREATED | Người dùng → Thêm (có thể gán SYS_ADMIN) | Có |
| USER_UPDATED | Người dùng → Sửa (họ tên, email, điện thoại, cho phép đăng nhập) | Có |
| USER_SOFT_DELETED | Người dùng → Xóa | Có |
| USER_ROLES_UPDATED | Người dùng → Vai trò | Có |
| USER_PASSWORD_CHANGED | Đổi mật khẩu (Admin); không lưu hash | Có |
| CUSTOMER_CREATED / CUSTOMER_UPDATED | Khách hàng → Thêm/Sửa (gồm bật/tắt hoạt động) | Có |
| CUSTOMER_PORTAL_MEMBERSHIP_SAVED / …_ACTIVE_CHANGED | Tài khoản Customer Portal (cấp, đổi vai trò, thu hồi, kích hoạt lại) | Có |

Không audit: đăng nhập (chỉ log ứng dụng, có throttle), đổi mật khẩu trong Customer Portal (ứng dụng khác, chưa có cơ chế audit — PENDING_APPROVAL).

## Hành vi
- `Security__Audit__RequireDurableAudit=false` (mặc định hiện tại): thay đổi vẫn được lưu nếu không ghi được audit; ứng dụng ghi cảnh báo `DURABLE_AUDIT_NOT_ENFORCED` khi khởi động và báo trên giao diện. Lỗi làm hỏng transaction (doomed) luôn rollback.
- `=true`: bảng thiếu/không nhìn thấy, sai cột, hoặc INSERT thất bại (thiếu quyền, ràng buộc…) → **rollback toàn bộ thay đổi**, người dùng thấy thông báo "Không thể ghi nhật ký audit…", không lộ chi tiết SQL.

## Các bước
1. **Backup** TAPortalSys theo quy trình vận hành.
2. **Xác minh schema** (chỉ đọc): mở `database/scripts/007-verify-audit-logs.sql` trong SSMS/DBeaver với login db_owner, đặt `@AppPrincipal` = database user của ứng dụng Admin, chạy. Ghi lại `VERIFY_STATUS`.
3. **Xác minh principal**: các dòng PASS/FAIL cho visibility, INSERT AuditLogs, quyền Users/UserRoles/Customers/CustomerPortalMemberships. `NOT_VERIFIED` nghĩa là không impersonate được — chạy lại với quyền phù hợp.
4. **Chỉ khi 007 xác nhận bảng thực sự không tồn tại** (chạy bằng db_owner) và đã được duyệt: chạy `database/migrations/010-create-audit-logs-if-missing.sql`. Không chạy lại migration 006.
5. **Cấp quyền tối thiểu**: theo `database/scripts/009-grant-admin-audit-insert-template.sql` — chỉ `GRANT INSERT ON dbo.AuditLogs` cho user ứng dụng Admin (không SELECT/UPDATE/DELETE; dashboard sẽ báo "chỉ có quyền ghi", đây là chấp nhận được).
6. **Xác minh lại**: chạy 007 tới khi `VERIFY_STATUS=PASS` (hoặc WARN đã được chấp nhận bằng văn bản).
7. **Bật cấu hình**: đặt biến môi trường `Security__Audit__RequireDurableAudit=true` cho site Admin (ngoài Git), khởi động lại; xác nhận log không còn `DURABLE_AUDIT_NOT_ENFORCED`.
8. **Kiểm tra rollback** trên môi trường test: thu hồi tạm quyền INSERT của user ứng dụng (DBA), thử sửa một user test → phải thấy thông báo lỗi audit và dữ liệu user không đổi; cấp lại quyền, thử lại → thành công và có dòng `USER_UPDATED`.

## Rollback cấu hình
Đặt lại `Security__Audit__RequireDurableAudit=false` và khởi động lại site; dữ liệu không bị ảnh hưởng.
