# Change ADMIN-ROLE-PERMISSIONS-CHG-20261107T030206Z-6AC11F

- Feature ID: `ADMIN-ROLE-PERMISSIONS`
- Change ID: `ADMIN-ROLE-PERMISSIONS-CHG-20261107T030206Z-6AC11F`
- Created UTC: 2026-11-07T03:02:06.802350+00:00
- Tiêu đề: TA Portal HR: employees, login accounts per employee, departments, teams, job titles
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator (2026-11-07): T.A Portal là portal nội bộ; cần quản lý nhân viên, tài khoản đăng nhập của nhân viên và phân quyền tài khoản (phân quyền đã có ở change 9C2068). Customer Portal không làm phân quyền.

Sửa (TAPortal.Web, dùng các bảng nhân sự đã có sẵn trong TAPortalSys — schema `documents/TAPortalSys_initial_hr_core.sql`, không cần migration):
- Nhóm menu "Nhân sự": Nhân viên, Phòng ban, Nhóm, Chức danh (hiện theo quyền .VIEW).
- Nhân viên: danh sách + tìm kiếm/lọc (chi nhánh, phòng ban, trạng thái, có tài khoản); tạo (mã gợi ý từ NumberSequences EMPLOYEE_CODE, NV + 6 số), sửa thông tin cơ bản (mã không đổi), phân công hiện tại (EmployeeAssignments: một dòng primary/current, đổi phân công đóng dòng cũ ValidTo và tạo dòng mới), đổi trạng thái (EmployeeStatusHistories; RESIGNED/TERMINATED ⇒ IsActive=0, EndDate), thông tin riêng tư (CCCD, MST cá nhân, BHXH, địa chỉ) chỉ đọc/ghi với HR.EMPLOYEE.VIEW_PRIVATE/UPDATE_PRIVATE, audit chỉ ghi tên trường riêng tư đã đổi; xóa mềm (từ chối khi còn gắn tài khoản).
- Tài khoản đăng nhập của nhân viên (HR.EMPLOYEE.ACCOUNT_LINK): tạo tài khoản mới gắn với nhân viên (cần thêm AUTH.USERS.CREATE; chính sách mật khẩu, vai trò, chỉ SYS_ADMIN cấp SYS_ADMIN — dùng chung PortalDb.InsertUserAsync với trang Người dùng), gắn tài khoản có sẵn chưa gắn, gỡ liên kết. Trang Người dùng có cột Nhân viên.
- Danh mục Phòng ban / Nhóm / Chức danh: tạo, sửa, xóa mềm (từ chối khi còn nhân viên đang được phân công).
- Quyền HR.EMPLOYEE.*, HR.DEPARTMENT.*, HR.TEAM.*, HR.JOB_TITLE.* (đã có trong dbo.Permissions) đưa vào danh mục quyền nhóm "Nhân sự" trên trang Vai trò.
- Mọi ghi: kiểm tra lại quyền trong SQL cùng transaction, RowVersion, audit EMPLOYEE_*/DEPARTMENT_*/TEAM_*/JOB_TITLE_*.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `src/TAPortal.Web/Controllers/EmployeesController.cs`, `Controllers/HrCatalogControllers.cs`, `Services/EmployeePolicy.cs`, `Services/EmployeeStore.cs`, `Services/HrCatalogStore.cs`, `Models/EmployeeModels.cs`, `Views/Employees/*`, `Views/HrCatalog/*`, `tests/TACustomerPortal.Tests/SafeAdminEmployeeTests.cs`
- MODIFY `src/TAPortal.Web/Security/AdminPermissions.cs`, `Services/PortalDb.cs`, `Controllers/PortalControllers.cs`, `Program.cs`, `Views/Shared/_Layout.cshtml`, `Views/Users/Index.cshtml`, `tests/TACustomerPortal.Tests/SafeAdminPermissionTests.cs`

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Không có migration (bảng và mã quyền đã có; đã kiểm tra cột/index trên production bằng truy vấn chỉ đọc, không đọc dữ liệu cá nhân).
- Execution: NOT_APPLICABLE

## Test và kết quả
- Release build 0 lỗi; Safe tests 562/562 sau khi gộp (31 test mới).

## Publish
- portal (TAPortal.Web). Xem report.

## Git
- Branch: feature/customer-portal-mvp01 (gộp nhánh agent c4db71c bằng merge 1305ac1)
- Commit SHA: xem report
- PR:
- Merge: xem report
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Câu INSERT/UPDATE mới chưa chạy với DB thật (test dùng fake); cần thử trên production với dữ liệu thử.
- Tạo tài khoản từ hồ sơ nhân viên cần thêm quyền AUTH.USERS.CREATE (operator có thể nới).
- Chưa làm: xuất Excel, chuyên môn, hồ sơ giấy tờ, người quản lý/nhóm cha, ngày thử việc/chính thức, quản lý chi nhánh/công ty.
- 4 nhân viên hiện có mang mã dạng TA… (dữ liệu thử); mã mới gợi ý NV000001.
