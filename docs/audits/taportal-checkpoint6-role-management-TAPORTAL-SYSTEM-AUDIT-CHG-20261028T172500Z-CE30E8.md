# Checkpoint 6 — Quản lý vai trò người dùng (AUD-12)

- CHANGE_ID: `TAPORTAL-SYSTEM-AUDIT-CHG-20261028T172500Z-CE30E8` (feature `TAPORTAL-SYSTEM-AUDIT`)
- Starting SHA `900db91ecb1671c072a958cd240ee12064cc76cb`; source SHA `36db1038ebe071ebab4f588538e118d79fb416e9`
- Agent: Claude Code (Claude Opus 5.5). Database: DATABASE_NOT_VERIFIED — MCP chỉ đọc `taportal-db` trả lỗi ở `db_ping`/`db_list_tables`; không chạy SQL nào.

## 1. Mô hình phân quyền hiện tại (đọc từ source + migration 002/006, seed 001)

| Câu hỏi | Hiện trạng trước Checkpoint 6 |
|---|---|
| Cấp vai trò thế nào | Chỉ khi tạo user (một `RoleId` trong modal) → một dòng `dbo.UserRoles` |
| Đổi sau khi tạo | Không có chức năng |
| Role ↔ permission | `UserRoles` → `Roles` → `RolePermissions` → `Permissions`; cộng thêm `UserPermissions` (ALLOW/DENY) trực tiếp theo user; DENY loại bỏ quyền khi dựng claims |
| Ai được cấp | Chỉ SYS_ADMIN (UsersController `SystemAdminOnly`); `PermissionHandler` coi SYS_ADMIN có mọi permission |
| Tự nâng quyền | Không có đường nào cho user thường (không có màn hình sửa vai trò; RolesController chỉ xem) |
| Mất SYS_ADMIN cuối cùng | Không thể qua UI (không có xóa vai trò); tự vô hiệu hóa đã bị chặn ở Checkpoint TAPORTAL-SYSTEM-AUDIT |
| Claims sau khi đổi quyền | Cookie Admin `OnValidatePrincipal` đọc lại user/vai trò/permission từ DB **mỗi request** và thay principal; user hết vai trò lẫn permission bị đăng xuất |
| Quyền ngoài Customer Portal | Vai trò Admin và `CustomerPortalMemberships` (OWNER/ADMIN/VIEWER) là hai bảng tách biệt; không có liên kết |
| Audit trail | Migration 006 định nghĩa `dbo.AuditLogs`, nhưng ứng dụng chưa ghi gì vào đó; sự tồn tại trong TAPortalSys chưa được xác minh |
| Cần migration | Không — chỉ cần controller/service/view |

## 2. Findings

| ID | Project | Module | Severity | Problem | Root Cause | Fix | Files | Test | Status |
|---|---|---|---|---|---|---|---|---|---|
| AUD-12 | Admin | Vai trò người dùng | MEDIUM | Không đổi được vai trò sau khi tạo user | Chưa triển khai | Màn hình `/users/roles/{id}` + `RoleChangePolicy` + `SqlUserRoleStore` | UserRoleStore.cs, PortalControllers.cs, Views/Users/*, PortalDb.cs, Program.cs | 20 test + smoke | FIXED (gán vai trò) |
| AUD-12a | Admin | Ma trận quyền | — | Sửa RolePermissions, tạo role mới, quản lý UserPermissions trực tiếp | Cần chính sách phân quyền mới | Không triển khai | — | — | PENDING_APPROVAL |
| AUD-25 | Admin | Audit | MEDIUM | Không xác minh được `dbo.AuditLogs` (và quyền INSERT) trong TAPortalSys | DB không truy cập được từ phiên này | Ghi audit có điều kiện trong cùng transaction; thiếu bảng → log ứng dụng + thông báo trên UI | UserRoleStore.cs | Kiểm tra tĩnh câu SQL | PENDING_OPERATOR |
| AUD-26 | Admin | Vai trò người dùng | LOW | Override `UserPermissions` (ALLOW/DENY) không hiển thị trên màn hình vai trò | Ngoài phạm vi | Ghi nhận | — | — | OPEN |

## 3. Business rules đã áp dụng

1. Chỉ SYS_ADMIN: `SystemAdminOnly` ở controller + policy `AUTH.ROLES.MANAGE` (SYS_ADMIN thỏa mãn) + **kiểm tra lại actor là SYS_ADMIN đang hoạt động từ DB** bên trong transaction (cookie còn claim nhưng đã bị thu hồi → 403).
2. Không tự nâng quyền: user không phải SYS_ADMIN không vào được màn hình; store từ chối actor không phải SYS_ADMIN kể cả khi gọi trực tiếp.
3. Không mất SYS_ADMIN cuối cùng: tính tập user hoạt động (IsActive=1, IsDeleted=0, Status='ACTIVE') đang giữ vai trò SYS_ADMIN hoạt động; từ chối nếu thay đổi làm tập này rỗng — áp dụng cả khi tự gỡ của chính mình. Tự gỡ được phép khi còn quản trị viên khác (đúng đề xuất của operator).
4. Không tạo vai trò mới, không sửa SYS_ADMIN role; chỉ chọn từ danh mục `Roles` đang hoạt động. ID không tồn tại/đã xóa/ngừng hoạt động → từ chối. Vai trò ngừng hoạt động đang được gán được giữ nguyên.
5. Cấp SYS_ADMIN cho người khác vẫn được phép với SYS_ADMIN (giữ nguyên hành vi lúc tạo user), có nhãn cảnh báo "Toàn quyền hệ thống".
6. Không đổi mật khẩu, username, trạng thái, thông tin doanh nghiệp hay membership: action chỉ bind `roleIds` + `stamp`; câu SQL chỉ đọc Users/Roles và ghi `UserRoles` (+ `AuditLogs`).
7. Đồng thời: stamp = SHA-256 danh sách vai trò đang gán; khác → "đã bị thay đổi bởi thao tác khác", tải lại dữ liệu mới. Transaction serializable + UPDLOCK/HOLDLOCK trên vai trò của user đích và mọi dòng SYS_ADMIN; deadlock/khóa trùng → Conflict, không ghi.
8. CSRF trên POST; lỗi trả 400 kèm thông báo tiếng Việt và hiển thị lại dữ liệu mới nhất; thành công → redirect + thông báo.
9. Quyền mới có hiệu lực từ request kế tiếp nhờ cơ chế xác thực lại cookie hiện có.

## 4. Customer Portal

Không đổi source/Shared của Customer Portal; không đọc/ghi `CustomerPortalMemberships`; vai trò Admin và OWNER/ADMIN/VIEWER vẫn tách biệt. Lưu ý vận hành: user dùng chung bảng `Users`, nên nếu SYS_ADMIN gán vai trò Admin cho một tài khoản đang dùng Customer Portal thì tài khoản đó sẽ đăng nhập được Admin — đây là quyết định có chủ đích của SYS_ADMIN, không phát sinh tự động.

## 5. Kiểm thử (SafeAdminRoleTests — 20 test, không SQL)

- Policy (8): non-SYS_ADMIN tự nâng quyền → Forbidden; role lạ/ngừng hoạt động → InvalidRole; SYS_ADMIN cuối cùng (kể cả tự gỡ) → LastSystemAdmin; gỡ được khi còn admin khác; cấp SYS_ADMIN; giữ vai trò ngừng hoạt động và NoChange; stamp cũ → Conflict, stamp không phụ thuộc thứ tự; hai admin sửa chéo đồng thời (mô hình tuần tự) không làm mất cả hai, gửi lại trang cũ → Conflict.
- SQL text (1): không chứa PasswordHash, CustomerPortalMemberships, `UPDATE dbo.Users`, không ghi Roles/Permissions/RolePermissions/UserPermissions; audit có guard `OBJECT_ID`.
- HTTP với store in-memory (11): SYS_ADMIN xem màn hình (stamp, no-store, vai trò ngừng hoạt động không chọn được); OWNER có permission AUTH.ROLES.MANAGE vẫn 403 (GET/POST); thiếu CSRF 400; overposting (PasswordHash, Username, IsActive, CustomerId, PortalRole, UserId) bị bỏ qua, mật khẩu/username/membership giữ nguyên; role lạ, GUID sai định dạng, stamp cũ, SYS_ADMIN cuối → 400 + thông báo, không ghi; actor bị thu hồi SYS_ADMIN sau khi mở trang → 403; user không tồn tại → 404.
- **Chưa xác minh tự động**: hành vi khóa/transaction/deadlock trên SQL Server thật; claims cập nhật ở request kế tiếp (dựa trên mã `OnValidatePrincipal`, cần UAT); ghi `AuditLogs` thật.

## 6. Checklist UAT (gộp vào đợt UAT tổng thể cuối cùng)

1. SYS_ADMIN → Người dùng: cột Vai trò hiển thị; bấm "Vai trò" của user test → gán/gỡ vai trò thường → thông báo thành công; user test thấy menu/quyền thay đổi ở lần tải trang kế tiếp.
2. Với chỉ một SYS_ADMIN hoạt động: thử gỡ SYS_ADMIN của chính mình → bị từ chối.
3. Mở cùng màn hình ở hai tab, lưu tab 1 rồi lưu tab 2 → tab 2 báo "đã bị thay đổi".
4. Đăng nhập user không phải SYS_ADMIN → `/users/roles/{id}` bị từ chối.
5. Kiểm tra `dbo.AuditLogs` có dòng `USER_ROLES_UPDATED` (actor, user đích, vai trò cũ/mới, IP); nếu bảng không có hoặc thiếu quyền INSERT → báo DBA (AUD-25). Nếu tài khoản SQL của Admin bị từ chối INSERT, thao tác đổi vai trò sẽ báo lỗi và không ghi gì (fail-closed).
6. Xác nhận membership Customer Portal và mật khẩu user test không đổi.
