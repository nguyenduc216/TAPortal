# Customer Portal MVP 01 — vận hành và nghiệm thu

## Chuẩn bị database

Codec target: SQL Server `WIN-HSHL54Q184K\MSSQLSERVER2014`, sole database `TAPortalSys`. No database creation or automated SQL mutation. Actual schema has not been audited in this checkpoint. Operator must back up and review the read-only audit `database/scripts/003-customer-portal-schema-audit.sql`. Check GUIDs, status columns and existing User-Customer mappings. If an equivalent mapping or incompatible membership table exists, stop for review. Do NOT rerun legacy migrations 001-007: they contain USE TAPortal.

After operator review/backup, select TAPortalSys explicitly in DBeaver or SSMS. Apply migration 008 ONLY if schema audit confirms it is required, then run read-only verification 004. It creates the membership table and two compatibility permissions, without updating Users/Customers. MVP 01 remains SYS_ADMIN-only for account management/global lists; customer OWNER/ADMIN/VIEWER never grants system administration. No Agent delegation.

Rollback: dừng Customer Portal và chức năng cấp tài khoản; khôi phục bản ứng dụng trước đó. Script `005-rollback-customer-portal.sql` chỉ xóa bảng membership khi rỗng và từ chối nếu có dữ liệu. Xuất/lưu trữ dữ liệu và xin review riêng trước bất kỳ thao tác xóa nào. Các permission được giữ để không mất lịch sử cấp quyền. Có thể giữ bảng mới khi rollback ứng dụng.

## Chạy thử

Từ thư mục repository, cung cấp `Database__ConnectionString` bằng secret store hoặc biến môi trường cho từng process. Giá trị phải trỏ tới cùng database đã audit; không commit connection string. Không dùng tài khoản read-only của DbMcp cho hai website.

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
# Cấu hình Database__ConnectionString bên ngoài repository.
dotnet run --project src/TAPortal.Web
# Mở terminal riêng, cấu hình kết nối tương ứng:
dotnet run --project src/TACustomerPortal.Web --launch-profile CustomerPortal
```

Customer Portal: https://localhost:7243. TAPortal: dùng địa chỉ do project hiện tại in ra. Chỉ SYS_ADMIN vào Customers → Tài khoản Customer Portal (`/CustomerPortalAccounts`). Chọn doanh nghiệp và tạo user mới hoặc chọn user hiện có; chọn OWNER/ADMIN/VIEWER. Một user có thể được gán nhiều lần cho các doanh nghiệp khác nhau. Thay vai trò sẽ kích hoạt membership; nút khóa chỉ thu hồi quyền trong doanh nghiệp đó, không khóa user dùng chung. Muốn khóa toàn bộ user, dùng quy trình quản trị user hiện có.

Khách hàng vào `/account/login`. Một membership được tự chọn; nhiều membership dẫn tới `/account/select-customer`. Các trang Dashboard, hồ sơ doanh nghiệp, cá nhân, đổi mật khẩu đều sẵn có. Dữ liệu tài chính hiển thị Chưa kích hoạt/Chưa có dữ liệu. Chưa có thanh toán, TINGEE, ngân hàng, webhook hoặc đối soát.

## Build, test, publish

```powershell
dotnet restore TAPortal.sln
dotnet build TAPortal.sln -c Release --no-restore
dotnet test TAPortal.sln -c Release --no-build
```

Checkpoint 1 uses SafePortalTests: real HTTP/cookie/CSRF/views with an in-memory ICustomerStore and invalid SQL configuration. No SQL connection. Legacy PortalTests are explicitly skipped and initialization is blocked because they create/drop LocalDB databases. Historical LocalDB results are not current evidence. CI performs restore/build/safe tests only, without LocalDB or publish. SQL CRUD/constraints/transactions still require operator verification.

## IIS độc lập

Checkpoint 3 authorizes artifact publishing only to `D:\T.A\TAPortal\TAPortal\publish\deployed-TACustomerPortal.Web`. All web projects follow `publish/deployed-<csproj filename without extension>`. Creating this exact folder is allowed after safety preflight. Admin publishing and IIS changes are outside this checkpoint. Inspect IIS mappings, existing artifacts and write permissions first; unreadable mappings block publishing. A live IIS target requires separate overwrite approval.

Thiết lập riêng từng app: `ASPNETCORE_ENVIRONMENT=Production`, `Database__ConnectionString`, `DataProtection__KeyPath` là đường dẫn bền vững nằm ngoài web root, cùng logging destination. Cấp ACL key folder chỉ cho đúng application pool identity; bật Load User Profile. Trên Windows key ring dùng DPAPI theo identity. Backup key ring theo chính sách vận hành. Các deployment nhiều máy cần cơ chế key encryption/certificate dùng chung trong từng ứng dụng, không chia key giữa hai ứng dụng. Mỗi site dùng thư mục log/thu thập stdout hoặc central logging riêng; mặc định application log đi qua ASP.NET logging providers. Không bật stdout vô hạn mà không có retention.

DBA tạo SQL identity riêng cho customer app, tham khảo `006-customer-portal-least-privilege.sql`: SELECT Users/Customers/Memberships, UPDATE đúng PasswordHash, UpdatedAt, UpdatedBy, LastLoginAt của Users; không DDL, không quyền quản trị, không ghi Customers/memberships/system roles. Admin app giữ identity phục vụ nghiệp vụ quản trị. Shared DB không có row-level SQL isolation theo customer: backend chịu trách nhiệm kiểm tra context; module nghiệp vụ tương lai phải truy vấn theo context đã xác minh, không nhận CustomerId tự do từ client.

Cookie TA.Customer.Auth có HttpOnly, SameSite=Lax, Secure ở production, phiên 2 giờ và key application name riêng. Membership/user/customer được kiểm tra mỗi request. Đổi mật khẩu vô hiệu phiên cũ ở cả hai ứng dụng. Sau nâng cấp, admin cookie cũ không có password stamp sẽ yêu cầu đăng nhập lại. SYS_ADMIN không có bypass membership ở Customer Portal.

Chống brute force: 10 POST login/phút/IP và 5 lần thử/phút/tài khoản chuẩn hóa, đều trong mỗi process. Tài khoản có/không tồn tại dùng cùng giới hạn; đổi IP không reset bộ đếm tài khoản. Trả 429 khi vượt giới hạn; tránh khóa user dài hạn do kẻ khác gây ra. Giới hạn này gồm cả lần đăng nhập hợp lệ.

Mặc định không xử lý forwarded headers. Chỉ khi chạy sau proxy, cấu hình `ReverseProxy__KnownProxies__0` (và các chỉ số tiếp theo) bằng IP socket thực của proxy tin cậy. Không dùng IP client, wildcard, hoặc dải rộng. Ứng dụng xử lý X-Forwarded-For/X-Forwarded-Proto tối đa một hop trước HTTPS redirect và rate limiting; header từ socket không tin cậy bị bỏ qua. Proxy phải ghi đè/xóa header client tự gửi và chặn client đi thẳng đến backend. Không bật biến auto-forwarding toàn cục hoặc thêm middleware khác bỏ qua chính sách này. Chuỗi nhiều proxy cần thiết kế/review riêng.

Với nhiều IIS instances, gateway/WAF phải có bộ đếm dùng chung: ít nhất IP throttle và credential/account throttle theo rủi ro. Kiểm tra trên staging bằng request từ proxy đã allowlist, nguồn ngoài allowlist, header giả và hai instances. Chưa xác minh WAF triển khai thực tế. Tests in-process xác nhận trusted/untrusted socket, header giả, đổi forwarded IP không bypass account throttle; không thay thế kiểm thử cấu hình mạng/IIS thực.

Tất cả POST dùng antiforgery; logout chỉ POST.

## Giới hạn xác minh

Current evidence: safe HTTP in-process tests and Release solution build. No live DB/schema/SQL CRUD or IIS/browser UAT validation in Checkpoint 1; no publish or deploy. Check HTTPS, persistent Data Protection keys, ACL, app identities, CDN access, multi-instance WAF/proxy behavior and all legacy Admin business flows during operator UAT. Earlier SQL LocalDB/publish results are historical only.

MVP 02: TINGEE, tài khoản ngân hàng và Merchant cần contract, secret management và mô hình phân quyền theo tenant được review riêng.

## Checkpoint 1 manual SQL handoff

All paths below are relative to repository root. Codex did NOT execute these scripts.
1. Operator confirms server/database, backup and read-only audit: `database/scripts/003-customer-portal-schema-audit.sql`. Review Users/Customers/Permissions/Roles/UserRoles, auth/status columns, existing relationships.
2. Only if needed and approved: `database/migrations/008-customer-portal-memberships.sql`. Guard rejects a non-TAPortalSys database before starting a transaction. Do not duplicate or replay existing migrations blindly.
3. Read-only verification: `database/scripts/004-verify-customer-portal.sql`. Expect valid FKs, unique user/customer pair, allowed roles and no duplicates.
4. Review `database/scripts/006-customer-portal-least-privilege.sql` (commented grants guidance) before operator applies permissions to confirmed identities.
Rollback 005 is NOT an installation step; separate operator approval/backup is required. It checks TAPortalSys and refuses to drop populated memberships.

Tenant switching preserves original IssuedUtc/ExpiresUtc and cannot extend the two-hour session. Account/dashboard/profile pages send Cache-Control: no-store.

## Checkpoint 3 ? operator handoff
Operator reports migration 008 manually applied; database verification PASS_WITH_WARNINGS (4 Users, 10 Customers, 0 Memberships). Treat as VERIFIED_WITH_WARNINGS, operator-reported, not a new Codex SQL audit. Do not rerun migration 008. Customer account provisioning and functional acceptance remain pending.

IIS preflight on 2026-10-28: W3SVC/WAS running; applicationHost.config access denied; appcmd list vdir fails reading redirection.config due to insufficient permissions. Target does not yet exist. Publish is BLOCKED until an operator verifies all effective IIS physical paths (including overlap/aliases) and confirms target is not served, or grants read access for a new preflight. No automatic elevation or IIS edits.

Before deployment, confirm dedicated site/application name, dedicated No Managed Code app pool/identity, HTTPS hostname/port/certificate, installed .NET 8 Hosting Bundle, Production environment, external secret configuration for Database__ConnectionString to TAPortalSys, external persistent DataProtection__KeyPath, least-privilege ACLs, log destination/retention, backup and rollback ownership. Operator sets these outside Git. Confirm trusted proxy IPs if applicable, secure cookies/HTTPS redirect and login throttling/WAF. After separately authorized IIS deployment, perform browser login/provisioning, tenant isolation, password/session revocation and Admin regression acceptance. No site creation, binding changes, pool recycle/start or secrets changes in Checkpoint 3.
