# Change ADMIN-ROLE-PERMISSIONS-CHG-20261107T020425Z-9C2068

- Feature ID: `ADMIN-ROLE-PERMISSIONS`
- Change ID: `ADMIN-ROLE-PERMISSIONS-CHG-20261107T020425Z-9C2068`
- Created UTC: 2026-11-07T02:04:25.260457+00:00
- Tiêu đề: Admin portal: role management with per-function permissions, permission-based access instead of SYS_ADMIN only
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator (2026-11-07): trang quản lý T.A Portal chưa có quản lý tài khoản + phân quyền thật — cần phân được quyền cho tài khoản thực hiện những chức năng nào trên hệ thống.

Trước đây: có trang Người dùng (tạo/sửa/xóa mềm/gán vai trò) nhưng trang Vai trò chỉ xem; gần như mọi controller khóa `SystemAdminOnly` nên tài khoản không phải SYS_ADMIN không dùng được gì.

Sửa:
- Danh mục quyền trong code `Security/AdminPermissions.cs` (22 mã, nhãn tiếng Việt, nhóm theo menu): Tổng quan; Khách hàng (VIEW/CREATE/UPDATE, SECRETS.REVEAL, PORTAL.MANAGE); Đối soát (VIEW/MANAGE); Gói dịch vụ (VIEW/MANAGE, PURCHASES.VIEW/MANAGE); Tích hợp (SANDBOX, LOGS.VIEW, NOTIFICATIONS.MANAGE); Hệ thống (AUTH.USERS.*, AUTH.ROLES.VIEW/MANAGE, SYSTEM.MENUS.MANAGE).
- Trang Vai trò & phân quyền: danh sách (số người dùng, số quyền), tạo vai trò (mã `[A-Z0-9_]{2,30}`), sửa tên/mô tả/hoạt động + ma trận quyền theo nhóm ("Chọn tất cả"), xóa mềm khi không còn người dùng; SYS_ADMIN chỉ xem; audit ROLE_CREATED/UPDATED/SOFT_DELETED; chống ghi đè đồng thời.
- Mọi controller dùng policy `PERM:<mã>` thay cho SystemAdminOnly (SYS_ADMIN vẫn đủ quyền); test `EveryAdminActionRequiresACataloguePermission` bắt mọi action phải có quyền.
- Kiểm tra lại quyền trong SQL (`TAPortal.Shared/ActorPermissionSql.cs`) cho các store trước đây chỉ nhận SYS_ADMIN (MembershipAdminStore, role/account store); chỉ SYS_ADMIN cấp/thu hồi SYS_ADMIN hoặc sửa tài khoản đang giữ SYS_ADMIN; tạo người dùng kèm SYS_ADMIN chỉ khi người tạo là SYS_ADMIN.
- Menu trái và nút theo quyền; thanh trên hiện tên vai trò; sau đăng nhập về trang đầu tiên được phép; trang từ chối có nút về trang được phép.
- Shared: `PortalRoleText` có nhãn MEMBER ("Thành viên (phân quyền riêng)"), admin giữ nguyên MEMBER khi lưu (change 95622D).

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `database/migrations/028-admin-permission-catalogue.sql`, `database/scripts/045-verify-admin-permission-catalogue.sql`, `database/scripts/046-rollback-admin-permission-catalogue.sql`
- ADD `src/TAPortal.Web/Security/AdminPermissions.cs`, `Controllers/RolesController.cs`, `Services/RoleAdminStore.cs`, `Models/RoleModels.cs`, `Views/Roles/Edit.cshtml`, `src/TAPortal.Shared/ActorPermissionSql.cs`, `tests/TACustomerPortal.Tests/SafeAdminPermissionTests.cs`
- MODIFY các controller của TAPortal.Web, `_Layout.cshtml`, `AccountController`, `Program.cs`, views Users/Customers/ServicePackages/Roles, store trong TAPortal.Shared (MembershipAdminStore, PortalRoleText, role/account policy), test Safe Admin.

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- `028-admin-permission-catalogue.sql`: chỉ INSERT mã quyền còn thiếu vào dbo.Permissions và quyền còn thiếu của SYS_ADMIN vào dbo.RolePermissions; mọi dòng thêm có CreatedBy = marker A0000028-…-000000000028 để rollback 046 chỉ xóa đúng các dòng đó (từ chối nếu vai trò khác/quyền trực tiếp/menu còn dùng). Tên quyền ASCII (sqlcmd không đọc UTF-8), giao diện dùng nhãn tiếng Việt trong code. Verify 045.
- Execution: xem report

## Test và kết quả
- Release build 0 lỗi; Safe tests 553/553 sau khi gộp hai phần (44 test mới phần Admin).

## Publish
- TAPortal.Shared thay đổi ⇒ portal và invoice (và webhooks theo quy tắc republish). Xem report.

## Git
- Branch: feature/customer-portal-mvp01 (gộp từ nhánh worktree của agent)
- Commit SHA: xem report
- PR:
- Merge: xem report
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Người có AUTH.ROLES.MANAGE tích được mọi quyền trong danh mục, kể cả cho vai trò mình đang giữ (gần như mọi quyền trừ SYS_ADMIN) — cần operator quyết có giới hạn hay không.
- Chưa ẩn theo quyền: form trên trang đối soát, tab Tingee của khách hàng, link KPI trên tổng quan (server vẫn từ chối).
- Store SQL mới chỉ kiểm thử bằng fake và kiểm tra câu SQL, chưa chạy với DB thật ngoài dry-run migration.
