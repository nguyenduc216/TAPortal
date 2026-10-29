# Change TAPORTAL-SYSTEM-AUDIT-CHG-20261029T164706Z-E9FB56

- Feature ID: `TAPORTAL-SYSTEM-AUDIT`
- Change ID: `TAPORTAL-SYSTEM-AUDIT-CHG-20261029T164706Z-E9FB56`
- Created UTC: 2026-10-29T16:47:06Z
- Tiêu đề: Cross-app UX consistency: Customer Portal role names and password UX, shared fixes, remaining screens
- Trạng thái: IMPLEMENTED / BUILD_VERIFIED / LOCAL_PUBLISH_COMPLETE (Admin + Customer) / VISUAL_NOT_VERIFIED (production) / PRODUCTION_NOT_DEPLOYED
- Agent: Claude Code (claude-opus-5-5)

## Yêu cầu và phạm vi
Operator: sửa luôn trang Customer và rà soát mọi chức năng còn lại của hai hệ thống để xử lý triệt để (theo chuẩn: không mask modal, vai trò hiển thị chữ, dropdown tìm kiếm, icon ô nhập, kiểm tra mật khẩu realtime).

## Rà soát (screen inventory) và kết quả
| Hệ thống | Màn hình | Phát hiện | Xử lý |
|---|---|---|---|
| Customer | Tổng quan, Hồ sơ doanh nghiệp, Chọn doanh nghiệp | Vai trò hiện mã OWNER/ADMIN/VIEWER | Hiện tên (PortalRoleText, chuyển sang Shared) |
| Customer | Đổi mật khẩu | Không icon, không hiện/ẩn, không quy tắc realtime, không kiểm tra khớp | Icon + hiện/ẩn + quy tắc xanh/đỏ + "Khớp với mật khẩu mới" |
| Customer | Chọn doanh nghiệp | Không tìm kiếm khi có nhiều doanh nghiệp | Ô tìm kiếm không dấu |
| Cả hai | ta-design.css | Animation để lại transform (gốc của lỗi mask modal) | fill-mode backwards trong file dùng chung; bỏ override Admin |
| Admin | Hồ sơ cá nhân | Hiện mã vai trò (SYS_ADMIN) | Hiện tên vai trò (tra danh mục vai trò) |
| Admin | Topbar | Người không phải SYS_ADMIN thấy mã vai trò | "Người dùng TAPortal" |
| Admin | Gán vai trò người dùng | Chip mã vai trò cạnh tên | Chỉ hiện tên (mã giữ ở data-attribute) |
| Admin | Vai trò & phân quyền, Quản lý menu | Không tìm kiếm | Ô tìm kiếm + bộ đếm |
| Admin | Đổi mật khẩu | Không kiểm tra xác nhận khớp realtime | Quy tắc "Khớp với mật khẩu mới" |
| Admin | Tài khoản Portal (workspace + trang cũ) | Kích hoạt lại membership khi khách hàng ngừng hoạt động chỉ bị chặn ở UI | Chặn ở backend controller (thu hồi vẫn cho phép) |
| Cả hai | Login, Users, Customers, Workspace, modal | Đã đạt chuẩn ở change trước | Kiểm tra lại: icon/dropdown/test đều PASS |

Giữ nguyên có chủ đích: cột "Mã vai trò" (danh mục Vai trò), "Mã menu", "Mã quyền" (danh mục kỹ thuật dành cho quản trị); 6 dịch vụ "Chưa kích hoạt" trên Customer dashboard (không thêm module).

## File thay đổi
- Shared: RENAME src/TAPortal.Web/Services/PortalRoleText.cs → src/TAPortal.Shared/PortalRoleText.cs
- Dùng chung (bản giống hệt hai app): wwwroot/css/ta-design.css, wwwroot/js/ta-shell.js (taCheckPassword, taCheckConfirm)
- Customer: Views/Account/{ChangePassword,SelectCustomer}, Views/Dashboard/Index, Views/Profile/Company, wwwroot/js/customer.js
- Admin: Controllers/{AccountController,CustomerPortalAccountsController,PortalControllers}.cs; Views/Account/{ChangePassword,Profile}, Views/{Roles,Menus}/Index, Views/Users/Roles, Views/Shared/_Layout; wwwroot/css/ta-admin.css, wwwroot/js/ta-portal.js
- Tests: SafeCustomerWorkspaceTests (+1 reactivation guard; icon test mở rộng hai app), SafePortalTests (tên vai trò)

## Migration SQL
- Không có. NOT_EXECUTED (không áp dụng)

## Test và kết quả
- restore OK; Release build 0/0; safe tests 253/253 PASS; Codec unittest OK, validate PASS; git diff --check OK
- Visual: ảnh có tương tác (đổi mật khẩu Customer/Admin, tìm doanh nghiệp, tìm vai trò, dashboard/hồ sơ Customer); VISUAL_NOT_VERIFIED trên môi trường thật

## Publish
- TAPortal.Web: exit 0, 2026-10-29T16:51:08Z→16:51:11Z, 62 files, SHA256 9b428ced25d913d3acdd7a62ff2e86f70308a6168ceefb698e47bd47ac22e8bd
- TACustomerPortal.Web: exit 0, 2026-10-29T16:51:11Z→16:51:13Z, 58 files, SHA256 de8af4e6e36bf8f5222b6bf2d156df06f3933c4510d7ffa8bc3fea7486b0d947
- Shared DLL ac0a8bef3f06074b7db29a76deb130b4b74fffa571deb6b61cabf38e5834d5ea ở cả hai (cùng source f38fd5b7a93dd5388cf7660dbd01ece9d85ef691); ta-design.css trùng hash hai app; không secret
- Backup publish\rollback\TAPORTAL-SYSTEM-AUDIT-CHG-20261029T164706Z-E9FB56\{TAPortal.Web (50265e30…), TACustomerPortal.Web (b39bb31c…)}, xác minh trùng 100%
- PUBLISHED (local) / NOT_DEPLOYED; IIS NOT_CHECKED

## Git
- Source SHA f38fd5b7a93dd5388cf7660dbd01ece9d85ef691; base bc86feef865d40a7f54be6653f71d5e9a59b75e8; delivery SHA báo sau push; PR #7 draft, merge PENDING_APPROVAL

## Còn lại / operator quyết định
- Trang tổng hợp /CustomerPortalAccounts và KPI dashboard trỏ tới nó (giữ để tương thích).
- Danh sách "tài khoản có sẵn" gồm cả nhân viên TA (quy tắc nghiệp vụ chưa có).
- Phân trang server-side; CSP chặt hơn (cần bỏ inline handler); tab tương lai của workspace.
- Rollback: chép hai thư mục backup; git revert f38fd5b7a93dd5388cf7660dbd01ece9d85ef691.
