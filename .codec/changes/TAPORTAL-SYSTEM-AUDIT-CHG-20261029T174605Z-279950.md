# Change TAPORTAL-SYSTEM-AUDIT-CHG-20261029T174605Z-279950

- Feature ID: `TAPORTAL-SYSTEM-AUDIT`
- Change ID: `TAPORTAL-SYSTEM-AUDIT-CHG-20261029T174605Z-279950`
- Created UTC: 2026-10-29T17:46:05Z
- Tiêu đề: Customer Portal login and feature audit: data-unavailable handling, redirects, error diagnostics
- Trạng thái: FIXED_IN_CODE / BUILD_VERIFIED / LOCAL_PUBLISH_COMPLETE (Customer) / ROOT_CAUSE_ON_PRODUCTION NOT_VERIFIED / PRODUCTION_NOT_DEPLOYED
- Agent: Claude Code (claude-opus-5-5)

## Báo lỗi
Operator: đăng nhập Customer Portal (Production) luôn ra trang "Không thể xử lý yêu cầu", kể cả khi mật khẩu sai — chưa thấy kiểm tra mật khẩu.

## Phân tích (audit)
- Pipeline Customer chỉ hiển thị /account/error khi có exception chưa xử lý (UseExceptionHandler, không có status-code pages).
- POST /account/login gọi store.FindUser (SQL) TRƯỚC khi kiểm tra mật khẩu → mọi lỗi truy cập dữ liệu (thiếu Database__ConnectionString: InvalidOperationException; không kết nối được SQL / thiếu quyền SELECT dbo.Users, dbo.CustomerPortalMemberships, dbo.Customers / thiếu bảng: SqlException) đều thành trang lỗi chung cho cả mật khẩu đúng và sai → khớp triệu chứng.
- Nguyên nhân cụ thể trên Production: NOT_VERIFIED (MCP read-only không kết nối được; agent không được kiểm tra IIS). Giả thuyết theo thứ tự khả năng: (1) site Customer chưa có Database__ConnectionString; (2) tài khoản SQL của Customer thiếu quyền (template 006 chưa áp dụng đúng); (3) không kết nối được SQL Server từ app pool Customer.

| ID | Mức | Vấn đề | Sửa |
|---|---|---|---|
| CL-01 | HIGH | Lỗi dữ liệu khi đăng nhập → trang lỗi chung, không thông báo, không mã tra log | Bắt lỗi dữ liệu, giữ form, thông báo + mã sự cố, HTTP 503, log CUSTOMER_LOGIN_DATA_UNAVAILABLE (không ghi thông tin đăng nhập) |
| CL-02 | MEDIUM | Ghi LastLoginAt lỗi (vd. thiếu quyền UPDATE) làm hỏng lần đăng nhập đã thành công | Chỉ cảnh báo log, vẫn đăng nhập |
| CL-03 | MEDIUM | Redirect cứng "/" và "/account/select-customer" sai khi chạy dưới IIS virtual directory | Url.Content("~/") / RedirectToAction |
| CL-04 | LOW | Quá số lần thử: trang trắng 429 | Thông báo trên form (429); limiter theo IP trả lời văn bản |
| CL-05 | LOW | Trang Cá nhân: user null → NullReference | Đăng xuất + về trang đăng nhập |
| CL-06 | LOW | Trang lỗi không có mã tra cứu | Hiển thị mã sự cố (Activity/TraceIdentifier) + nút về trang chính |
| CL-07 | LOW | Thiếu connection string không có dấu hiệu khi khởi động | Log Critical CUSTOMER_PORTAL_DB_NOT_CONFIGURED |
| CL-08 | LOW | Chọn doanh nghiệp khi không có doanh nghiệp: danh sách trống không giải thích | Empty state |
Đã kiểm tra, không lỗi: đổi mật khẩu (sai → thông báo chung; đúng → đăng xuất, về đăng nhập), đăng xuất, cô lập doanh nghiệp (chọn doanh nghiệp không thuộc quyền → 403), xác thực lại cookie mỗi request, điều hướng mềm (trang đăng nhập không thuộc shell → tải thường).

## File thay đổi
- src/TACustomerPortal.Web/Controllers/{AccountController,DashboardController,ProfileController}.cs, Program.cs, Views/Account/{Error,SelectCustomer}.cshtml
- ADD tests/TACustomerPortal.Tests/SafeCustomerLoginResilienceTests.cs (6)

## Migration SQL
- Không có. NOT_EXECUTED

## Test
- Release build 0/0; safe tests 260/260 PASS; Codec unittest OK, validate PASS; git diff --check OK

## Publish
- TACustomerPortal.Web: exit 0, 2026-10-29T17:48:30Z→17:48:33Z, 55 files, SHA256 d70a3736abaa9c0058d879496f40243a1323cb0156ef0ecf62af58a13afb95c9; không secret
- Backup publish\rollback\TAPORTAL-SYSTEM-AUDIT-CHG-20261029T174605Z-279950\TACustomerPortal.Web (ac27314e…, xác minh trùng)
- Admin NOT_REQUIRED (cc224fd3… giữ nguyên). Shared DLL hai app khác hash chỉ do InformationalVersion; source src/TAPortal.Shared không đổi kể từ 123c2ff
- NOT_DEPLOYED; IIS NOT_CHECKED

## Operator: xác định nguyên nhân trên Production
1. Event Viewer → Windows Logs → Application: tìm lỗi của TACustomerPortal (".NET Runtime"/"ASP.NET Core") quanh thời điểm đăng nhập; sau khi deploy bản này tìm CUSTOMER_LOGIN_DATA_UNAVAILABLE hoặc CUSTOMER_PORTAL_DB_NOT_CONFIGURED kèm mã sự cố hiển thị trên form.
2. Kiểm tra biến môi trường Database__ConnectionString của site/app pool Customer (trỏ TAPortalSys).
3. Kiểm tra quyền SQL của tài khoản Customer: SELECT dbo.Users, dbo.Customers, dbo.CustomerPortalMemberships; UPDATE dbo.Users (LastLoginAt, PasswordHash) — xem database/scripts/006.
4. Có thể bật tạm stdoutLogEnabled="true" trong web.config để xem lỗi khởi động, tắt lại sau.

## Git
- Source SHA 3cd60409165d1163bcfccbec94acf7397700df92; base 8fd3a16656160a03816a1d757f0114db7b504c98; delivery SHA báo sau push; PR #7 chưa merge
