# Báo cáo audit hệ thống TAPortal — TAPORTAL-SYSTEM-AUDIT-CHG-20261028T163541Z-822BA2

- FEATURE_ID: `TAPORTAL-SYSTEM-AUDIT` (phụ thuộc `CUSTOMER-PORTAL-FOUNDATION`)
- Branch: `feature/customer-portal-mvp01`; starting SHA `24d9f586831d9fafa52866b5b927472aa3c62f80`; source SHA `336ab7c80552e733a5df9e6c4612aeb163037007`
- Thực hiện bởi: Claude Code (Claude Opus 5.5), theo AGENTS.md + Codec. Ngày 2026-10-28 (UTC).

## 1. Phạm vi và phương pháp

Đã đọc source của TAPortal.Web (Admin), TACustomerPortal.Web và TAPortal.Shared, gồm: Program, controllers, PortalDb/CustomerStore/MembershipAdminStore, PasswordService, policy phân quyền, các view Razor, `ta-portal.js`, migration 002/007/008 và seed 001/002 để đối chiếu schema/permission, cùng test hiện có. Không truy cập database (không có truy vấn đọc trong lần này — `NOT_VERIFIED`), không kiểm tra IIS/server.

Phân loại kết quả: **lỗi đã chứng minh** (đọc trực tiếp từ source), **rủi ro tiềm ẩn**, **chưa triển khai**, **hạn chế môi trường**.

Đính chính: bảng phân loại sơ bộ đưa ra trong hội thoại trước khi sửa đã xếp ba mục thiếu menu/thiếu chức năng sửa vào CRITICAL. Sau khi xác minh, không mục nào là lỗ hổng bảo mật hay gây mất dữ liệu, nên được hạ xuống HIGH/MEDIUM. Nhận định "thiếu validation mật khẩu phía server" cũng không đúng: server có gọi `PasswordPolicy.Validate`. Bảng dưới đây là kết quả cuối cùng.

## 2. Bảng findings

| ID | Project | Module | Severity | Problem | Root Cause | Fix | Files | Test | Status |
|---|---|---|---|---|---|---|---|---|---|
| AUD-01 | Admin | Người dùng | HIGH | Danh sách người dùng chỉ có Thêm, không thể sửa họ tên/email/điện thoại/trạng thái | `UsersController` chỉ có Index + Create; `PortalDb` không có hàm cập nhật | Thêm Edit GET/POST (policy `SystemAdminOnly` + `AUTH.USERS.UPDATE`, CSRF), `UpdateUserAsync` chỉ ghi DisplayName/Email/Phone/IsActive; không đụng Username/PasswordHash/Status/vai trò; kiểm tra trùng email; chặn tự vô hiệu hóa | PortalControllers.cs, PortalDb.cs, PortalModels.cs, Views/Users/Edit.cshtml, Views/Users/Index.cshtml | SafeAdminCrudTests (phân quyền, CSRF, overposting, validation); smoke anonymous 302 | FIXED (luồng ghi SQL: PENDING_OPERATOR UAT) |
| AUD-02 | Admin | Khách hàng | HIGH | Không thể thêm/sửa doanh nghiệp; nút Thêm bị `disabled` | `CustomersController` chỉ có Index | Thêm Create/Edit (policy `CRM.CUSTOMERS.CREATE/UPDATE`), mã KH bất biến sau khi tạo, chuẩn hóa IN HOA, kiểm tra trùng mã + bắt lỗi unique index, bật/tắt IsActive, giữ dữ liệu khi lỗi validation | PortalControllers.cs, PortalDb.cs, PortalModels.cs, Views/Customers/Edit.cshtml, Views/Customers/Index.cshtml | SafeAdminCrudTests; smoke anonymous 302 | FIXED (luồng ghi SQL: PENDING_OPERATOR UAT) |
| AUD-03 | Admin | Khách hàng (view) | MEDIUM | Nút "Tài khoản Customer Portal" render trên tiêu đề trang, trước `@model` | Thẻ `<a>` nằm ở dòng 1 của view | Chuyển vào khu vực hành động của page-header | Views/Customers/Index.cshtml | Build Razor | FIXED |
| AUD-04 | Admin | Navigation | MEDIUM | Sidebar không có mục quản lý tài khoản Customer Portal | Chỉ có link phụ trong trang Khách hàng | Thêm mục menu (chỉ SYS_ADMIN) + nút "Tài khoản" theo từng doanh nghiệp | _Layout.cshtml, Customers/Index.cshtml | Smoke 302; phân quyền qua test | FIXED |
| AUD-05 | Admin | Tài khoản Customer Portal | MEDIUM | View không theo design system; ký tự `?` thay dấu phân cách (mất encoding); không có UI lọc theo doanh nghiệp dù backend hỗ trợ `customerId`; chọn được user/doanh nghiệp ngưng hoạt động rồi nhận lỗi chung; không có thông báo sau thu hồi/kích hoạt | View viết tối giản; ký tự `—` bị ghi thành `?` | Viết lại theo card/table Tabler, bộ lọc, disable mục ngưng hoạt động, thông báo TempData, giữ bộ lọc sau redirect | Views/CustomerPortalAccounts/Index.cshtml, CustomerPortalAccountsController.cs | Build; test CSRF/phân quyền | FIXED |
| AUD-06 | Admin | Người dùng (tạo) | MEDIUM | Lỗi validation làm mất dữ liệu đã nhập (redirect); trùng email gây SqlException 500 (unique index `UX_Users_NormalizedEmail`) | Redirect + không kiểm tra email | Render lại Index (400), mở lại modal với dữ liệu trừ mật khẩu; kiểm tra email + bắt 2601/2627/547 | PortalControllers.cs, Users/Index.cshtml, ta-portal.js | Test CSRF; render cần UAT | FIXED |
| AUD-07 | Admin | Navigation | LOW | Vai trò/Menu hiển thị cho người không có quyền (→ access denied); "Cấu hình hệ thống", "Nhật ký hoạt động" là link chết `href="#"` | Menu không kiểm tra permission; placeholder | Ẩn theo permission; đổi placeholder thành mục disabled "Sắp có" | _Layout.cshtml | Build | FIXED |
| AUD-08 | Admin | Danh sách | LOW | Badge trạng thái khách hàng luôn màu "active" và in mã thô; badge người dùng bỏ qua `Status` | View dùng sai trường | Badge = `IsActive && Status=='ACTIVE'` (cùng điều kiện đăng nhập) | Users/Index, Customers/Index, PortalDb.GetCustomersAsync | Build | FIXED |
| AUD-09 | Codec | Operating contract | MEDIUM | Quy định chỉ ghi "Codex"; "Checkpoint 3 chỉ cho phép Customer" mâu thuẫn yêu cầu publish hai dự án; thiếu quy tắc publish theo ảnh hưởng của Shared, phương pháp manifest, tách source/delivery SHA, cấu trúc báo cáo thống nhất, phối hợp đồng thời | Contract được viết dần theo checkpoint | Thêm `MULTI-AGENT CONTRACT` vào AGENTS.md + SKILL.md; config `agents`/`report`/quy tắc publish; validator + 8 mutation test mới | AGENTS.md, SKILL.md, .codec/config.json, codec.py, test_codec.py | Codec tests 4/4; validate PASS | FIXED |
| AUD-10 | Codec/Docs | Encoding | LOW | Tiêu đề "Local publish policy ? Checkpoint 3" ở 4 file | Ký tự `—` bị ghi thành `?` | Khôi phục `—` | AGENTS.md, SKILL.md, .codec/README.md, runbook | Kiểm tra byte | FIXED |
| AUD-11 | Codec | Artifact hash | MEDIUM | Thứ tự sắp xếp của manifest chưa được định nghĩa: cùng 54 file Customer cũ cho `40442b…` (thứ tự `Path` không phân biệt hoa/thường trên Windows) nhưng `f76b42…` (thứ tự ordinal) | Mô tả phương pháp không chốt thứ tự | Contract chốt thứ tự ordinal theo đường dẫn POSIX; ghi cả hai digest của artifact cũ | AGENTS.md, SKILL.md | Tái lập cả hai digest | FIXED |
| AUD-12 | Admin | Vai trò người dùng | MEDIUM | Không có UI đổi vai trò sau khi tạo (chỉ chọn lúc tạo) | Chưa triển khai | Không sửa: cần quy tắc nghiệp vụ (ai được cấp SYS_ADMIN, bảo vệ SYS_ADMIN cuối cùng, audit) | — | — | OPEN (PENDING_APPROVAL) |
| AUD-13 | Admin | Đăng nhập | MEDIUM | Admin không giới hạn tần suất đăng nhập (Customer Portal có 10/phút/IP và 5/phút/tài khoản) | Chưa triển khai | Đề xuất dùng lại cơ chế throttle của Customer Portal ở checkpoint riêng | — | Grep Program.cs: 0 tham chiếu | OPEN |
| AUD-14 | Admin | Cache | LOW (rủi ro) | Trang Admin đã xác thực không đặt `Cache-Control: no-store` (Customer Portal có) | Chưa triển khai | Đề xuất bổ sung cùng AUD-13 | — | Grep Program.cs | OPEN |
| AUD-15 | Admin | Người dùng | LOW | Tài khoản có `Status` khác `ACTIVE` không thể bật lại qua UI (chỉ sửa được IsActive) | Không có danh mục Status được định nghĩa (không có CHECK) | Không tự đặt quy tắc; hiển thị Status chỉ đọc | — | — | OPEN |
| AUD-16 | Admin | Khách hàng | LOW | MST không được kiểm tra trùng (index không unique) | Quy tắc nghiệp vụ chưa rõ | Không tự áp quy tắc | — | — | OPEN |
| AUD-17 | Admin | Người dùng/Khách hàng | LOW | Không có xóa (soft delete) trên UI | Chưa triển khai; vô hiệu hóa (IsActive) đã thu hồi quyền truy cập | Giữ nguyên để tránh mất dữ liệu | — | — | OPEN |
| AUD-18 | Admin | Dashboard | LOW | Thẻ "Cơ sở dữ liệu: Kết nối" là giá trị cố định, không phản ánh trạng thái thực | Placeholder | Đề xuất health check thực | — | — | OPEN |
| AUD-19 | Admin | Tạo người dùng | LOW | Có thể cấp SYS_ADMIN từ dropdown mà không có cảnh báo | Thiết kế (chỉ SYS_ADMIN được tạo) | Thêm nhãn "(toàn quyền hệ thống)" + gợi ý; ẩn vai trò ngưng hoạt động | Users/Index.cshtml | Build | FIXED |

Tổng: 19 findings — CRITICAL 0, HIGH 2, MEDIUM 8, LOW 9. FIXED 12; OPEN 7 (gồm 1 PENDING_APPROVAL). Không có mục nào là VERIFIED, vì các luồng ghi SQL đã xác thực chưa được chạy trên database thật.

## 3. Customer Portal (TACustomerPortal.Web) và Shared

Rà soát lại AccountController/Dashboard/Profile, CustomerSecurity, CustomerStore. Không phát hiện lỗi mới: tenant lấy từ cookie được bảo vệ và đối chiếu backend mỗi request; tham số `customerId` trên URL bị bỏ qua; SYS_ADMIN không bypass membership; CSRF, throttle và `no-store` vẫn còn. 34 SafePortalTests vẫn PASS. Shared không thay đổi; Customer vẫn được publish lại theo yêu cầu bàn giao hai artifact (assembly nhúng commit SHA nên hash mới khác).

## 4. Kiểm thử bổ sung (SafeAdminCrudTests — 31 case, không SQL)

- Anonymous → 302 login cho các route Users/Edit, Customers/Create, Customers/Edit, CustomerPortalAccounts.
- Principal có vai trò OWNER kèm đủ permission chi tiết (AUTH.USERS.UPDATE, CRM.CUSTOMERS.*, CUSTOMERS.PORTAL.MANAGE) vẫn bị 403 ở cả GET/POST — vai trò Customer Portal/permission không bao giờ mở được quản trị SYS_ADMIN.
- SYS_ADMIN POST không có token → 400 trước khi chạm dữ liệu (6 endpoint).
- Reflection: mọi action POST của Admin có `[ValidateAntiForgeryToken]`; Users/Customers/CustomerPortalAccounts có `SystemAdminOnly`; Edit/Create khai báo permission chi tiết.
- Overposting: `UserEditVm` không có Password/Role; Username/Status là `[ValidateNever]` và bị controller ghi đè từ DB.
- Validation DataAnnotations cho người dùng và mã khách hàng.

Hạn chế: `PortalDb` là class SQL cụ thể, nên render trang Admin khi đã đăng nhập và các câu lệnh UPDATE/INSERT chưa được kiểm thử tự động. Các phần này cần UAT thủ công của operator.

## 5. UAT thủ công đề xuất (operator, trên TAPortalSys với tài khoản test được phê duyệt)

1. SYS_ADMIN → Người dùng → Sửa: đổi họ tên/email/điện thoại, lưu, kiểm tra danh sách; nhập email trùng → thông báo lỗi, dữ liệu giữ nguyên; thử tắt chính mình → bị chặn.
2. Tắt "Cho phép đăng nhập" của một user test → phiên Admin/Customer của user đó bị từ chối ở request kế tiếp.
3. Khách hàng → Thêm (mã trùng → lỗi), Sửa (mã chỉ đọc), tắt hoạt động → membership của doanh nghiệp không đăng nhập được Customer Portal.
4. Tài khoản Customer Portal: lọc theo doanh nghiệp, tạo mới, gán user có sẵn cho doanh nghiệp thứ hai, đổi vai trò, thu hồi/kích hoạt lại; kiểm tra Customer Portal tương ứng (UAT-01…UAT-09 trong docs/customer-portal-checkpoint4-uat.md).
5. Kiểm tra trực quan desktop/tablet/mobile.
