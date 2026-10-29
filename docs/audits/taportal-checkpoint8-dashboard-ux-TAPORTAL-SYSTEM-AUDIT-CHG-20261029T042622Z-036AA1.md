# Checkpoint 8 — Dashboard, UX consistency, admin workflow (AUD-18, AUD-26, AUD-30..34)

- CHANGE_ID `TAPORTAL-SYSTEM-AUDIT-CHG-20261029T042622Z-036AA1`; starting SHA `20558b4b56be9dc1f1f527366e9e0952f451cfcd`; source SHA `2013ea2a10be1f4f618a65c9733adabfda1cc873`
- Agent: Claude Code (Claude Opus 5.5). Database không truy cập (DATABASE_NOT_VERIFIED). Trình duyệt: BROWSER_NOT_VERIFIED.

## 1. AUD-18 theo báo cáo gốc
Báo cáo `taportal-system-audit-TAPORTAL-SYSTEM-AUDIT-CHG-20261028T163541Z-822BA2.md`: *"Dashboard — Thẻ 'Cơ sở dữ liệu: Kết nối' là giá trị cố định, không phản ánh trạng thái thực. Nguyên nhân: Placeholder. Đề xuất health check thực."*
Kiểm tra lại source (`Views/Home/Index.cshtml` tại 20558b4): ngoài badge "Kết nối", badge "Ứng dụng: Hoạt động" và "Phân quyền: RBAC" cũng cố định; khung "Hoạt động gần đây" là placeholder; KPI "Người dùng" đếm mọi user chưa xóa (gồm cả vô hiệu hóa/khóa) mà không nêu định nghĩa; khi DB lỗi thì cả trang chuyển sang trang lỗi chung.

## 2. Bảng findings

| Finding | Root cause | Affected files | Risk | Fix | Verification | Status |
|---|---|---|---|---|---|---|
| AUD-18 (LOW) | Placeholder cố định, KPI không định nghĩa, không có error state | Home/Index.cshtml, HomeController, PortalDb.GetDashboardAsync | Hiển thị sai trạng thái hệ thống | `IDashboardStore`/`SqlDashboardStore`: KPI có định nghĩa, trạng thái DB/audit thực tế, cấu hình audit/throttle, thay đổi gần đây từ AuditLogs (nếu đọc được); trạng thái lỗi/trống tường minh | 10 test HTTP/service + smoke | FIXED (SQL thật NOT_VERIFIED) |
| AUD-26 (LOW) | UserPermissions không hiển thị | Users/Roles.cshtml, UserRoleStore | Admin không thấy quyền thực tế | Bảng chỉ đọc ALLOW/DENY | HTTP test (không có input) | FIXED (sửa trực tiếp PENDING_APPROVAL) |
| AUD-30 (LOW) | `collapse navbar-collapse show` không có toggler | _Layout.cshtml | Mobile luôn bung menu | Nút `navbar-toggler`, `<nav id="taSidebarMenu">` | Markup test | FIXED (BROWSER_NOT_VERIFIED) |
| AUD-31 (LOW) | AccessDenied luôn trỏ "Về tổng quan" (chỉ SYS_ADMIN) | Account/AccessDenied.cshtml | Vòng lặp 403 | Link theo quyền: tổng quan / hồ sơ / đăng nhập | HTTP test 3 trường hợp | FIXED |
| AUD-32 (LOW) | Membership chỉ xét cờ của chính nó | CustomerPortalAccounts/Index.cshtml | Hiểu nhầm quyền thực | `MembershipDisplay.Evaluate` theo đúng điều kiện đăng nhập của Customer Portal + lý do | 8 unit case | FIXED |
| AUD-33 (LOW) | Link xóa hiện cho chính mình; thiếu cảnh báo SYS_ADMIN cuối | Users/Edit.cshtml, Users/Roles.cshtml | UX | Ẩn link; ghi chú trên trang vai trò (server vẫn chặn) | HTTP test | FIXED |
| AUD-34 (LOW) | Icon không `aria-hidden`, link active không `aria-current`, toggle tài khoản không có tên | _Layout.cshtml, ta-portal.js | Trợ năng | Bổ sung thuộc tính | Markup test | FIXED |

## 3. Định nghĩa KPI (DashboardSql)
| KPI | Định nghĩa | Ghi chú |
|---|---|---|
| Người dùng hiện hành | được đăng nhập + vô hiệu hóa + bị khóa | không gồm xóa mềm |
| Được đăng nhập | `IsDeleted=0 AND IsActive=1 AND Status='ACTIVE'` | đúng `AccountStatusPolicy` |
| Đã vô hiệu hóa | `IsDeleted=0 AND IsActive=0` | |
| Bị khóa theo trạng thái | `IsDeleted=0 AND IsActive=1 AND (Status IS NULL OR Status<>'ACTIVE')` | |
| Xóa mềm (lịch sử) | `IsDeleted=1` | nhãn riêng |
| Khách hàng hiện hành | `IsDeleted=0`, chia hoạt động (`IsActive=1 AND Status='ACTIVE'`) / ngừng | |
| Quyền Customer Portal có hiệu lực | membership `IsActive=1` + user đủ điều kiện + customer hoạt động, chưa xóa | một dòng/membership (UQ UserId–CustomerId), tài khoản đếm `DISTINCT`; bảng không có → "Chưa có dữ liệu" |
| Vai trò đang hoạt động | `Roles IsDeleted=0 AND IsActive=1` | |
| Thay đổi gần đây | 5 dòng mới nhất từ AuditLogs (Action, thời điểm, tên actor); không đọc Old/NewValues | bảng thiếu / không có quyền SELECT / rỗng → mỗi trường hợp có thông báo riêng |

User/Customer KPI là một lần tổng hợp trên một bảng (không JOIN) nên không thể đếm trùng; ba nhóm user loại trừ lẫn nhau (test phân hoạch).

## 4. UX đã rà soát (không đổi quyền hay quy tắc nghiệp vụ)
- Users: danh sách/nhãn trạng thái/vai trò, sửa (stamp, trạng thái thực), xóa (xác nhận, chặn tự xóa/SYS_ADMIN cuối), vai trò (đang gán, ngừng hoạt động, tự sửa, SYS_ADMIN cuối, xung đột, quyền trực tiếp).
- Customers: MST giữ định dạng, thông báo trùng/sai định dạng, nhãn trạng thái. Không triển khai xóa mềm doanh nghiệp; không chạy migration 009.
- Customer Portal Accounts: trạng thái hiệu lực thực tế; OWNER/ADMIN/VIEWER không cấp quyền Admin.
- Navigation: link KPI đều tới route có thật và được bảo vệ (test ẩn danh → 302 login, không 404); menu "Sắp có" vẫn disabled.
- Không thêm CDN/script mới; JS chỉ thêm `aria-current`.

## 5. Kiểm thử — SafeAdminDashboardUxTests (24 test, không SQL)
Phân hoạch trạng thái và khớp quy tắc đăng nhập; tổng hiện hành loại xóa mềm; SQL KPI không JOIN/DISTINCT membership/không đọc OldValues; hiệu lực membership (8 case); dashboard hiển thị số định dạng vi-VN, nhãn định nghĩa, không còn badge "Kết nối", tên actor được HTML-encode; lỗi DB → trạng thái lỗi, không lộ chi tiết, không số giả; 4 empty state; link KPI hợp lệ; OWNER 403; layout có toggler/landmark/không icon thiếu aria-hidden/toggle tài khoản có tên; AccessDenied 3 trường hợp; trang vai trò có quyền trực tiếp chỉ đọc và cảnh báo SYS_ADMIN cuối; link xóa ẩn với chính mình.
Hồi quy: 136 test trước đó PASS (AUD-12/13/14/15/16/17/20/21/22/27/28, Customer Portal). Smoke artifact 15/15.

## 6. UAT (gộp vào đợt UAT tổng thể)
1. Mở Tổng quan với dữ liệu thật: đối chiếu số với truy vấn chỉ đọc theo bảng định nghĩa ở mục 3.
2. Ngắt kết nối DB (môi trường test) → trang hiển thị "Không tải được số liệu", không lộ chi tiết.
3. Trình duyệt mobile/tablet: nút menu mở/đóng sidebar; bàn phím Tab qua menu, focus nhìn thấy được.
4. Trang vai trò của user có UserPermissions → bảng chỉ đọc đúng ALLOW/DENY.
5. Tài khoản Customer Portal của doanh nghiệp đã ngừng → "Không hiệu lực" kèm lý do.
6. User không phải SYS_ADMIN vào trang không có quyền → nút "Về hồ sơ cá nhân".
