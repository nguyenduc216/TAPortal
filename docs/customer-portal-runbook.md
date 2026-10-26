# Customer Portal MVP 01 — vận hành và nghiệm thu

## Chuẩn bị database

Không ứng dụng nào tự chạy migration. Chưa xác minh schema của database triển khai thực tế. DBA phải xác định server/database từ cấu hình của TAPortal (có thể là TAPortalSys), sao lưu, rồi chạy `database/scripts/003-customer-portal-schema-audit.sql` chỉ đọc. So sánh GUID, cột trạng thái, quan hệ User–Customer hiện có với báo cáo audit. Nếu đã có quan hệ tương đương, dừng và điều chỉnh mapping trước khi áp dụng. Không chạy lại migration 001–007 vào database đang hoạt động vì các script cũ chứa USE TAPortal.

Sau khi review trên staging: chọn database rõ ràng trong SSMS/sqlcmd, chạy `database/migrations/008-customer-portal-memberships.sql`, rồi `database/scripts/004-verify-customer-portal.sql`. Migration tạo duy nhất bảng CustomerPortalMemberships cùng hai permission CUSTOMERS.VIEW và CUSTOMERS.PORTAL.MANAGE. Không thay đổi dữ liệu Users/Customers. MVP 01 chỉ cho SYS_ADMIN quản trị tài khoản portal và xem overview/danh sách Customers/Users toàn hệ thống. Hai permission được giữ cho tương thích migration nhưng không đủ để mở các trang này. Không cấp SYS_ADMIN cho Agent/nhân viên hạn chế để vượt giới hạn; chưa có delegated Agent scope. Customer OWNER/ADMIN chỉ là vai trò trong doanh nghiệp, không cấp quyền quản trị TAPortal.

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
dotnet publish src/TAPortal.Web/TAPortal.Web.csproj -c Release --no-build -o publish/admin
dotnet publish src/TACustomerPortal.Web/TACustomerPortal.Web.csproj -c Release --no-build -o publish/customer
```

Tests yêu cầu Windows + SQL LocalDB MSSQLLocalDB, tự tạo database ngẫu nhiên `TACustomerPortal_Test_<guid>` rồi xóa đúng database đó. Không nhận connection string production. CI Windows riêng chạy bộ kiểm thử này; CI build Linux hiện có tiếp tục được giữ. Không dùng database test như môi trường triển khai.

## IIS độc lập

Cài .NET 8 Hosting Bundle. Tạo hai IIS sites, application pools riêng (No Managed Code), identity riêng, physical paths tương ứng publish/admin và publish/customer. Cấp HTTPS binding theo domain của môi trường, production environment và AllowedHosts phù hợp; không cần sửa mã nguồn để đổi domain. web.config được SDK sinh khi publish. Không dùng chung thư mục publish, cookie hoặc key ring.

Thiết lập riêng từng app: `ASPNETCORE_ENVIRONMENT=Production`, `Database__ConnectionString`, `DataProtection__KeyPath` là đường dẫn bền vững nằm ngoài web root, cùng logging destination. Cấp ACL key folder chỉ cho đúng application pool identity; bật Load User Profile. Trên Windows key ring dùng DPAPI theo identity. Backup key ring theo chính sách vận hành. Các deployment nhiều máy cần cơ chế key encryption/certificate dùng chung trong từng ứng dụng, không chia key giữa hai ứng dụng. Mỗi site dùng thư mục log/thu thập stdout hoặc central logging riêng; mặc định application log đi qua ASP.NET logging providers. Không bật stdout vô hạn mà không có retention.

DBA tạo SQL identity riêng cho customer app, tham khảo `006-customer-portal-least-privilege.sql`: SELECT Users/Customers/Memberships, UPDATE đúng PasswordHash, UpdatedAt, UpdatedBy, LastLoginAt của Users; không DDL, không quyền quản trị, không ghi Customers/memberships/system roles. Admin app giữ identity phục vụ nghiệp vụ quản trị. Shared DB không có row-level SQL isolation theo customer: backend chịu trách nhiệm kiểm tra context; module nghiệp vụ tương lai phải truy vấn theo context đã xác minh, không nhận CustomerId tự do từ client.

Cookie TA.Customer.Auth có HttpOnly, SameSite=Lax, Secure ở production, phiên 2 giờ và key application name riêng. Membership/user/customer được kiểm tra mỗi request. Đổi mật khẩu vô hiệu phiên cũ ở cả hai ứng dụng. Sau nâng cấp, admin cookie cũ không có password stamp sẽ yêu cầu đăng nhập lại. SYS_ADMIN không có bypass membership ở Customer Portal.

Chống brute force: 10 POST login/phút/IP và 5 lần thử/phút/tài khoản chuẩn hóa, đều trong mỗi process. Tài khoản có/không tồn tại dùng cùng giới hạn; đổi IP không reset bộ đếm tài khoản. Trả 429 khi vượt giới hạn; tránh khóa user dài hạn do kẻ khác gây ra. Giới hạn này gồm cả lần đăng nhập hợp lệ.

Mặc định không xử lý forwarded headers. Chỉ khi chạy sau proxy, cấu hình `ReverseProxy__KnownProxies__0` (và các chỉ số tiếp theo) bằng IP socket thực của proxy tin cậy. Không dùng IP client, wildcard, hoặc dải rộng. Ứng dụng xử lý X-Forwarded-For/X-Forwarded-Proto tối đa một hop trước HTTPS redirect và rate limiting; header từ socket không tin cậy bị bỏ qua. Proxy phải ghi đè/xóa header client tự gửi và chặn client đi thẳng đến backend. Không bật biến auto-forwarding toàn cục hoặc thêm middleware khác bỏ qua chính sách này. Chuỗi nhiều proxy cần thiết kế/review riêng.

Với nhiều IIS instances, gateway/WAF phải có bộ đếm dùng chung: ít nhất IP throttle và credential/account throttle theo rủi ro. Kiểm tra trên staging bằng request từ proxy đã allowlist, nguồn ngoài allowlist, header giả và hai instances. Chưa xác minh WAF triển khai thực tế. Tests in-process xác nhận trusted/untrusted socket, header giả, đổi forwarded IP không bypass account throttle; không thay thế kiểm thử cấu hình mạng/IIS thực.

Tất cả POST dùng antiforgery; logout chỉ POST.

## Giới hạn xác minh

Đã có kiểm thử HTTP in-process và SQL LocalDB; đây không thay thế UAT trên schema/database thực tế hoặc IIS. Chưa kết nối database thực tế, chưa chạy migration thực tế, chưa deploy production. Kiểm tra HTTPS bindings, ACL, identity, log routing và tải CDN Tabler trong mạng triển khai trước khi nghiệm thu. Các project/flow cũ được build và publish; chưa thể khẳng định toàn bộ nghiệp vụ TAPortal trên production từ tests mới.

MVP 02: TINGEE, tài khoản ngân hàng và Merchant cần contract, secret management và mô hình phân quyền theo tenant được review riêng.
