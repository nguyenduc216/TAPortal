# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261107T020426Z-95622D

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261107T020426Z-95622D`
- Created UTC: 2026-11-07T02:04:26.054158+00:00
- Tiêu đề: Customer Portal: staff accounts and per-function permissions (Xem / Quản lý)
- Trạng thái: DEPLOYED

## Yêu cầu và phạm vi
Operator (2026-11-07): Customer Portal cần quản lý tài khoản + phân quyền: doanh nghiệp tự thêm tài khoản nhân viên và tích quyền theo từng chức năng, hai mức Xem / Quản lý (operator chọn), có mẫu nhanh.

Sửa:
- `Security/CustomerAccess.cs`: module ORDERS, CONTACTS, TRANSACTIONS, BANKS, PAYMENT_SETTINGS, EINVOICE, NOTIFICATIONS, PACKAGES, PROFILE, MEMBERS (Tổng quan luôn xem được). OWNER/ADMIN quản lý tất cả; VIEWER xem tất cả trừ MEMBERS; MEMBER theo chuỗi `Permissions` ("ORDERS:M;CONTACTS:V"). Mẫu: Quản trị, Kế toán, Bán hàng, Chỉ xem.
- `[CustomerModule]` trên từng controller: không có quyền xem ⇒ trang 403 thân thiện `ModuleDenied`; mọi `CanManage(m)` cũ đổi thành `CanManage(m, module)`; sidebar, ô tổng quan, link chéo và nút ẩn theo quyền.
- Trang "Tài khoản & phân quyền" (`/members`): danh sách, tạo tài khoản MEMBER (mật khẩu theo PasswordPolicy, tên đăng nhập/email không trùng trong dbo.Users, bắt đổi mật khẩu lần đầu), ma trận quyền, ngừng/kích hoạt, đặt mật khẩu mới. Không sửa OWNER, không tự sửa mình, người quản lý không phải OWNER/ADMIN không cấp quá quyền mình có; tài khoản thuộc doanh nghiệp khác không đặt lại mật khẩu ở đây. Audit CUSTOMER_MEMBER_*; mọi truy vấn lọc theo CustomerId.
- `CustomerStore.Memberships` đọc Permissions (tự chạy được trước migration 029 cho OWNER/ADMIN/VIEWER).

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `database/migrations/029-customer-member-permissions.sql`, `database/scripts/047-verify-customer-member-permissions.sql`, `database/scripts/048-rollback-customer-member-permissions.sql`
- ADD `src/TACustomerPortal.Web/Security/CustomerAccess.cs`, `Services/CustomerMemberStore.cs`, `Models/MemberModels.cs`, `Controllers/MembersController.cs`, `Views/Members/*`, `Views/Shared/ModuleDenied.cshtml`, `tests/TACustomerPortal.Tests/SafeCustomerMembersTests.cs`
- MODIFY 10 controller, `CustomerStore.cs`, `Program.cs`, `_Layout.cshtml`, `_ViewImports.cshtml`, nhiều view, `customer.css`, `customer.js`, `database/scripts/004-verify-customer-portal.sql` (chấp nhận MEMBER), test Safe liên quan.

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- `029-customer-member-permissions.sql`: thêm `Permissions varchar(500) NULL` vào dbo.CustomerPortalMemberships, CK_CPM_Role nhận thêm 'MEMBER'; không đổi dòng nào. Verify 047, rollback 048 (từ chối khi đã có MEMBER hoặc Permissions).
- Execution: EXECUTED 2026-11-07T02:07:33Z

## Test và kết quả
- Release build 0 lỗi; Safe tests 553/553 sau khi gộp (22 test mới phần Customer Portal).

## Publish
- TAPortal.Shared thay đổi ⇒ portal và invoice (và webhooks theo quy tắc republish). Xem report.

## Git
- Branch: feature/customer-portal-mvp01 (gộp từ nhánh worktree của agent)
- Commit SHA: c16e082 (gộp 3d90722 + 4066956)
- PR:
- Merge: fast-forward main
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- REVERTED 2026-11-07 theo yêu cầu operator (chỉ làm trên T.A Portal): xem `CUSTOMER-PORTAL-FOUNDATION-CHG-20261107T022032Z-9A2C1B`.
- Bốn điểm agent tự quyết cần operator xác nhận: không cấp quá quyền mình; không đặt lại mật khẩu cho tài khoản thuộc doanh nghiệp khác; bắt đổi mật khẩu lần đầu; lưu phân quyền cho tài khoản ADMIN/VIEWER chuyển nó thành MEMBER.
- VIEWER nay xem được trang Thông báo ở chế độ chỉ đọc.
- Chưa xem giao diện trên trình duyệt với dữ liệu thật.
