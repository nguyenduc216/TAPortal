# Báo cáo MVP 01 — 27/10/2026

**Trạng thái:** đã triển khai để review trên nhánh feature/customer-portal-mvp01. Chưa nghiệm thu production vì chưa kiểm tra schema triển khai và chưa chạy UAT/IIS. Không merge, không deploy, không sửa database production.

## A. Những gì đã triển khai

- Project ASP.NET Core MVC .NET 8 `src/TACustomerPortal.Web`, ngang cấp TAPortal.Web, trong solution hiện có.
- Thư viện `src/TAPortal.Shared`: PasswordService/PasswordPolicy dùng chung và MembershipAdminStore. Customer Portal không reference TAPortal.Web.
- TAPortal: CustomerPortalAccountsController và view quản lý tạo/gán tài khoản, đổi OWNER/ADMIN/VIEWER, thu hồi/kích hoạt membership, xem trạng thái user và LastLoginAt dùng chung. Tạo user và membership trong một SQL transaction serializable.
- Controllers Account, Dashboard, Profile; CustomerStore; CustomerCookieEvents và ICurrentCustomerContext.
- Giữ hai project Web/DbMcp hiện có; thêm workflow Windows kiểm thử LocalDB, giữ workflow Linux build hiện có.

## B. Database

Migration `008-customer-portal-memberships.sql` tạo bảng CustomerPortalMemberships: FK Users/Customers, unique UserId–CustomerId, role check, index theo customer, audit timestamps/actor. Membership được kích hoạt lại tại cùng dòng; không xóa dữ liệu Users/Customers.

Scripts 003 audit chỉ đọc, 004 verify, 005 rollback an toàn (từ chối nếu có membership), 006 mẫu quyền tối thiểu. Script mới không có USE/hardcode database. Đã chạy migration và xác minh trên database LocalDB test riêng; schema/database thực tế chưa được xác minh.

## C. Giao diện

Đăng nhập, chọn/chuyển doanh nghiệp, Dashboard, hồ sơ doanh nghiệp, cá nhân, đổi mật khẩu, access-denied/error; menu navy, nội dung sáng, card, CSS responsive, Tabler/Bootstrap 5, tiếng Việt. Module tài chính chỉ hiển thị Chưa kích hoạt/Chưa có dữ liệu. Có JavaScript chống submit trùng khi chờ response. Xem runbook để chạy localhost:7243 và cấp tài khoản qua TAPortal.

Đã kiểm tra HTTP/render Razor bằng integration tests; chưa kiểm tra trực quan trên browser desktop/tablet/mobile.

## D. Bảo mật

Xác thực Users dùng PBKDF2-SHA256 tương thích TAPortal. Cookie TA.Customer.Auth tách TA.Portal.Auth, application name Data Protection riêng, HttpOnly/SameSite Lax/Secure production, session 2 giờ. Kiểm tra user, customer, membership mỗi request. CustomerId được lấy từ cookie được bảo vệ và đối chiếu backend; tham số URL không thay đổi tenant. SYS_ADMIN không bypass membership.

POST có CSRF protection, logout hủy cookie, login giới hạn 10/phút/IP/process, thông báo lỗi chung và dummy hash cho username không tồn tại. Đổi mật khẩu bằng optimistic update, đăng xuất và làm cookie cũ mất hiệu lực cả hai website. Cập nhật admin cookie revalidation và kiểm tra quyền overview/customer; lọc role inactive khi lấy permissions. Đã bỏ fixed-password bootstrap khỏi startup và seed yêu cầu hash riêng từ operator. Connection string mặc định rỗng, không thêm secret thật.

## E. Kiểm thử

Đã chạy thành công trên máy phát triển:

- `dotnet restore TAPortal.sln`.
- `dotnet build TAPortal.sln -c Release --no-restore`: 0 lỗi, 0 cảnh báo.
- `dotnet test TAPortal.sln -c Release`: 24 pass, 0 fail, 0 skipped. SQL Server LocalDB thực và HTTP in-process, không dùng mock database.
- Publish độc lập TAPortal.Web và TACustomerPortal.Web vào publish/admin và publish/customer, với web.config IIS riêng.
- Kiểm thử bao gồm user A/B isolation, multi-customer switching, membership revocation, SYS_ADMIN không bypass, đăng nhập sai/khóa/xóa/không membership/customer khóa/xóa, đổi mật khẩu shared và invalidation session, CSRF/logout, cookie attributes, rate limit, provisioning admin, cấm customer đăng nhập admin, FK/unique/role check, transaction rollback, migration idempotency và giữ dữ liệu cũ.

Test result: `publish/test-results/customer-portal.trx` (artifact local, không commit). CI đã thêm nhưng kết quả GitHub Actions chưa được xác minh. Chưa chạy migration trên database thực tế, UAT toàn bộ TAPortal, kiểm thử IIS/HTTPS/ACL hoặc browser responsive trực quan. Không tuyên bố đã nghiệm thu các phần này.

## F. Pull Request

Draft PR vào main được tạo sau commit/push nhánh feature/customer-portal-mvp01. Xem URL trong phản hồi bàn giao và mục liên kết PR của repository. Không tự merge.

## G. Công việc tiếp theo

DBA review schema và migration trên staging; kiểm tra grants của nhân viên (CUSTOMERS.VIEW, CUSTOMERS.PORTAL.MANAGE), HTTPS, key persistence, log routing và UAT hai websites theo runbook. Nếu hệ thống chạy nhiều instances, bổ sung rate limit dùng chung tại gateway. MVP 02: TINGEE, ngân hàng và Merchant sau khi chốt contract và quyền theo tenant.
