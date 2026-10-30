# Checkpoint 5 — Bảo mật đăng nhập Admin và chống cache (AUD-13, AUD-14)

- CHANGE_ID: `TAPORTAL-SYSTEM-AUDIT-CHG-20261028T170619Z-0CBC7E` (feature `TAPORTAL-SYSTEM-AUDIT`)
- Starting SHA `1e1dda00255dd9fefa4db18ada8076f406e2bb68`; source SHA `183414102691ffb373d436951b27cf191d902fc2`
- Agent: Claude Code (Claude Opus 5.5). Không truy cập database, không kiểm tra IIS.

## 1. Hiện trạng trước khi sửa (đọc từ source tại 1e1dda0)

| Câu hỏi | Hiện trạng |
|---|---|
| Đăng nhập bằng gì | Username (`NormalizedUsername`), cookie `TA.Portal.Auth`, phiên trượt 8 giờ |
| Băm mật khẩu | PBKDF2-SHA256, 150.000 vòng (`TAPortal.Shared.PasswordService`) — không đổi |
| Rate limiter | Không có (`Program.cs` không có `AddRateLimiter`/throttle) |
| Account lockout | Không có |
| Phân biệt tài khoản tồn tại | Thông báo chung, nhưng **có qua thời gian xử lý**: `user is null || !Verify(...)` bỏ qua PBKDF2 khi username không tồn tại hoặc bị khóa |
| Log đăng nhập thất bại | Không có |
| Chống brute-force nhiều IP | Không có |
| Gợi ý tài khoản | Trang login in sẵn "Tài khoản khởi tạo: **admin**" |
| Cache | Chỉ trang login có `no-store` (do antiforgery); mọi trang quản trị, redirect, 403 không có Cache-Control |
| Access denied | Cookie `AccessDeniedPath=/account/access-denied` nhưng route thực là `/account/accessdenied` → **404** |
| Ảnh hưởng Customer Portal | Ứng dụng riêng, đã có throttle 10/phút/IP + 5/phút/tài khoản và no-store; không phụ thuộc thay đổi này |

## 2. Findings

| ID | Project | Module | Severity | Problem | Root Cause | Fix | Files | Test | Status |
|---|---|---|---|---|---|---|---|---|---|
| AUD-13 | Admin | Đăng nhập | MEDIUM | Không giới hạn số lần đăng nhập | Chưa triển khai | `AdminLoginThrottle`: 5 lần/5 phút/tài khoản (độc lập IP) + 20 lần/5 phút/IP socket; 429 + Retry-After; tự phục hồi | Security/AdminLoginThrottle.cs, AccountController.cs, Program.cs | 8 unit + 3 HTTP + smoke | FIXED |
| AUD-14 | Admin | Cache | LOW→MEDIUM | Trang quản trị đã xác thực có thể bị lưu bởi trình duyệt/proxy | Không có chính sách cache | Middleware tập trung sau `UseStaticFiles`: `no-store, no-cache, max-age=0`, `Pragma: no-cache`, `Expires: 0`; logout gửi `Clear-Site-Data: "cache"` | Program.cs, AccountController.cs | 7 HTTP + smoke | FIXED |
| AUD-20 | Admin | Đăng nhập | MEDIUM | Dò username qua thời gian phản hồi | Bỏ qua PBKDF2 khi không có user | Luôn verify với dummy hash | AccountController.cs | Unit (độc lập tồn tại) | FIXED |
| AUD-21 | Admin | Trang login | LOW | Công khai tên tài khoản quản trị `admin` | Ghi chú bootstrap còn sót | Xóa ghi chú | Views/Account/Login.cshtml | HTTP + smoke | FIXED |
| AUD-22 | Admin | Phân quyền | LOW | Người dùng thiếu quyền bị chuyển tới trang 404 | `AccessDeniedPath` sai route | Trỏ `/account/accessdenied` | Program.cs | Test tái hiện 404 trước khi sửa, PASS sau khi sửa; smoke | FIXED |
| AUD-23 | Admin | Đăng nhập | LOW | Còn chênh lệch thời gian nhỏ: user tồn tại phát sinh thêm 2 truy vấn vai trò/quyền | `GetAuthUserAsync` | Không sửa (cần đổi data access); chênh lệch ở mức ms so với ~100 ms PBKDF2 | — | — | OPEN |
| AUD-24 | Admin | Triển khai | MEDIUM | Bộ đếm nằm trong bộ nhớ từng process: reset khi recycle, không chia sẻ giữa instance; sau reverse proxy mọi client chung IP proxy | Thiết kế không lưu trạng thái, không tin X-Forwarded-For | Cần giới hạn tại gateway/WAF và quyết định trusted proxy (cần phê duyệt) | — | — | OPEN (PENDING_OPERATOR) |

## 3. Thiết kế AUD-13

- Thuật toán: cửa sổ cố định (fixed window) theo từng partition, trong bộ nhớ, dùng `TimeProvider`. Bản ghi hết hạn được dọn sau mỗi cửa sổ.
- Ngưỡng mặc định (đề xuất, chưa phải business rule): tài khoản 5 / 5 phút; IP 20 / 5 phút. Cơ sở: hệ thống quản trị có ít người dùng (operator báo 4 users), thường dùng chung NAT văn phòng, nên 20 lần/IP đủ cho vài quản trị viên nhập sai cùng lúc mà vẫn chặn việc xoay vòng username. Có thể ghi đè bằng `Security__AdminLogin__AccountPermitLimit`, `Security__AdminLogin__IpPermitLimit`, `Security__AdminLogin__Window`; giá trị ≤ 0 sẽ làm ứng dụng dừng khi khởi động.
- Partition: `acct:` + SHA-256(username đã trim, viết hoa), không phụ thuộc IP. `ip:` + SHA-256(IP socket), dùng `RemoteIpAddress`, không xử lý X-Forwarded-For. Không lưu username, mật khẩu hay IP ở dạng thô; log chỉ ghi 12 ký tự đầu của hash tài khoản.
- Thứ tự xử lý: antiforgery (400 nếu thiếu token, không bị đếm) → throttle → validation → tra cứu user → PBKDF2. Mọi POST đều bị đếm, kể cả lần đăng nhập đúng. Lần bị từ chối không bị đếm thêm và không kéo dài cửa sổ.
- Khi vượt ngưỡng: HTTP 429, `Retry-After` = số giây còn lại, trang login với thông báo chung "Bạn đã thử đăng nhập quá nhiều lần…", không kiểm tra thông tin đăng nhập, cùng hành vi cho tài khoản có và không tồn tại. GET login không bị ảnh hưởng.
- Đây là **giới hạn số lần thử (request)**, không phải khóa tài khoản theo số lần sai. Không khóa vĩnh viễn và không ghi SQL.
- Lý do không dùng `AddRateLimiter` có sẵn: .NET 8 `System.Threading.RateLimiting` không nhận `TimeProvider`, nên không thể kiểm thử phục hồi một cách tất định; partition theo tài khoản cần form đã bind. Không thêm package nào.
- Đánh đổi: kẻ tấn công biết một username có thể làm chậm tài khoản đó tối đa 5 phút cho mỗi cửa sổ. Không có khóa lâu dài, và quản trị viên vẫn có thể đăng nhập lại sau cửa sổ.

## 4. Thiết kế AUD-14

- Phạm vi: mọi response động của Admin — trang đã xác thực, login, redirect 302 tới login, 403, 429, trang lỗi, logout. File tĩnh được `UseStaticFiles` trả về trước middleware, nên giữ nguyên ETag/Last-Modified và không có `no-store`.
- Header: `Cache-Control: no-store, no-cache, max-age=0`, `Pragma: no-cache`, `Expires: 0`. Không có `public`, nên proxy dùng chung không được lưu.
- Logout: xóa cookie `TA.Portal.Auth`, gửi `Clear-Site-Data: "cache"` (Chrome/Edge/Firefox hỗ trợ; Safari hỗ trợ một phần), đồng thời redirect cũng có `no-store`.
- Giới hạn: `no-store` không thay thế phân quyền; phiên vẫn được kiểm tra lại với database mỗi request. Một số trình duyệt có back/forward cache có thể hiển thị lại trang trong bộ nhớ khi bấm Back — trang đó không thao tác được vì mọi request tiếp theo đều cần cookie hợp lệ. Cần UAT trên trình duyệt thật.

## 5. Kiểm thử (SafeAdminSecurityTests — 19 test, đồng hồ giả lập, không SQL)

- Unit: dưới ngưỡng cho qua; lần thứ 6 bị từ chối với `RetryAfter` chính xác; phục hồi đúng 5 phút; lần bị từ chối không kéo dài cửa sổ; tài khoản không phụ thuộc IP và được chuẩn hóa; giới hạn IP chặn xoay vòng username mà không ảnh hưởng IP khác; tài khoản có/không tồn tại xử lý giống nhau; chỉ lưu khóa hash và có dọn dẹp; cấu hình sai bị chặn khi khởi động (2).
- HTTP: 429 + Retry-After 300 + no-store, thông báo chung, phục hồi sau 5 phút; GET và POST bị CSRF từ chối không bị đếm; cô lập partition IP và bỏ qua X-Forwarded-For giả; no-store trên login/302/access-denied (4); LoginPath/AccessDeniedPath trả 200; file tĩnh giữ ETag và không có no-store; trang đã xác thực (SYS_ADMIN) và 403 (OWNER) có no-store; logout xóa cookie và gửi Clear-Site-Data.
- Hồi quy: 34 SafePortalTests (Customer Portal) + 31 SafeAdminCrudTests vẫn PASS.
- Smoke artifact Production (Kestrel, SQL cố ý sai): 11/11.

## 6. Checklist UAT thủ công (operator, tài khoản test được phê duyệt)

1. Nhập sai mật khẩu tài khoản test 5 lần → lần 6 hiện thông báo "quá nhiều lần", DevTools thấy 429 + Retry-After; đợi hết thời gian → đăng nhập đúng được.
2. Từ một máy, thử 20 username khác nhau → lần 21 bị 429; máy/IP khác vẫn đăng nhập được.
3. Đăng nhập SYS_ADMIN, mở Người dùng/Khách hàng → DevTools: `Cache-Control: no-store`; CSS/JS không có no-store.
4. Đăng xuất → bấm Back: không thao tác được dữ liệu, mọi click chuyển về login; response logout có `Clear-Site-Data`.
5. Tài khoản không có quyền SYS_ADMIN truy cập /users → trang "Không có quyền", không phải 404.
6. Nếu chạy sau reverse proxy: xác nhận IP proxy; bổ sung giới hạn tại gateway/WAF (AUD-24).
