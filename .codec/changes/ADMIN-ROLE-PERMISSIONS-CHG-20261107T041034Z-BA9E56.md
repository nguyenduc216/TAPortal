# Change ADMIN-ROLE-PERMISSIONS-CHG-20261107T041034Z-BA9E56

- Feature ID: `ADMIN-ROLE-PERMISSIONS`
- Change ID: `ADMIN-ROLE-PERMISSIONS-CHG-20261107T041034Z-BA9E56`
- Created UTC: 2026-11-07T04:10:34.649105+00:00
- Tiêu đề: Only SYS_ADMIN manages roles and assigns roles to accounts
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator (2026-11-07), trả lời câu hỏi giới hạn người có quyền "Quản lý vai trò": "Chỉ có admin phân quyền".

Sửa:
- AUTH.ROLES.MANAGE không còn cấp được: bỏ khỏi danh mục quyền trên trang Vai trò (dòng trong dbo.Permissions từ 028 vẫn giữ); `PermissionHandler` và `AdminPermissions.Has` chỉ coi SYS_ADMIN là có quyền này, bỏ qua claim được cấp; `ActorPermissionSql.HasPermissionAsync` kiểm tra lại trong SQL bằng `IsSystemAdminAsync` cho mã này (role store, role admin store).
- Gán vai trò khi tạo tài khoản (trang Người dùng và tạo tài khoản từ hồ sơ nhân viên): chỉ SYS_ADMIN; người khác không thấy ô chọn vai trò ("Vai trò do quản trị hệ thống gán sau khi tạo tài khoản"), bài gửi có vai trò bị từ chối (controller, EmployeePolicy.EvaluateRoles) và `PortalDb.CreateUserRoleSql` chỉ thêm vai trò khi người tạo là SYS_ADMIN đang hoạt động.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TAPortal.Shared/ActorPermissionSql.cs`
- MODIFY `src/TAPortal.Web/Security/PermissionAuthorization.cs`, `Security/AdminPermissions.cs`, `Services/PortalDb.cs`, `Services/EmployeePolicy.cs`, `Controllers/PortalControllers.cs`, `Views/Users/Index.cshtml`, `Views/Employees/Details.cshtml`
- MODIFY `tests/TACustomerPortal.Tests/SafeAdminPermissionTests.cs`, `SafeAdminEmployeeTests.cs`

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Không có migration.
- Execution: NOT_APPLICABLE

## Test và kết quả
- Release build 0 lỗi; Safe tests 562/562 (test cũ theo quy tắc AUTH.ROLES.MANAGE được cấp đổi sang: tài khoản có quyền đó vẫn bị 403, SYS_ADMIN làm được).

## Publish
- TAPortal.Shared đổi ⇒ portal, invoice, webhooks. Xem report.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: xem report
- PR:
- Merge: xem report
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Không có.
