# TAPortal System Audit — Báo cáo tổng kết (Checkpoint 10)

- CHANGE_ID `TAPORTAL-SYSTEM-AUDIT-CHG-20261029T052755Z-8ECA9D`; starting SHA `1ebbb016bc5891b61b5bdce4024ebfa470e888d1`; release candidate source SHA `95e64cedb14479850bc3f32d28ec0aa6c36f7d02`
- Nguồn đối chiếu: báo cáo audit tổng thể (822BA2), CP5 (0CBC7E), CP6 (CE30E8), CP7 (62724B), CP8 (036AA1), CP9 (E1EAC3), CUSTOMER-PORTAL-FOUNDATION (E64647, E6FF4A, 654653, 260F08), migrations/scripts, runbooks, 8 lớp safe test, git log.
- Môi trường xác minh: DATABASE_NOT_VERIFIED (MCP chỉ đọc lỗi kết nối suốt CP6–10), BROWSER_NOT_VERIFIED, PROXY_NOT_VERIFIED, IIS_NOT_VERIFIED, TIMING_NOT_VERIFIED.

## 1. Ma trận findings

Chú thích Final status: **CLOSED** = sửa trong code, có test, không phụ thuộc DB/hạ tầng/UAT ngoài kiểm tra trình duyệt thông thường; **FIXED_IN_CODE** = đã sửa và có test nhưng hành vi thật phụ thuộc DB/UAT chưa kiểm chứng; **PENDING_OPERATOR / PENDING_APPROVAL / NOT_VERIFIED** như định nghĩa Codec.

| Finding | Sev | Root cause (tóm tắt) | Code | DB | Hạ tầng | UAT | Final status |
|---|---|---|---|---|---|---|---|
| AUD-01 Sửa user | HIGH | Thiếu action/view | FIXED, VERIFIED_BY_TEST | Ghi SQL NOT_VERIFIED | — | NOT_RUN | FIXED_IN_CODE |
| AUD-02 Thêm/sửa doanh nghiệp | HIGH | Thiếu action/view | FIXED, VERIFIED_BY_TEST | NOT_VERIFIED | — | NOT_RUN | FIXED_IN_CODE |
| AUD-03 Vị trí nút | MEDIUM | Markup | FIXED | — | — | NOT_RUN | CLOSED |
| AUD-04 Menu Customer Portal | MEDIUM | Thiếu mục menu | FIXED | — | — | NOT_RUN | CLOSED |
| AUD-05 UI tài khoản CP | MEDIUM | View tối giản, `?` | FIXED | — | — | NOT_RUN | CLOSED |
| AUD-06 Mất dữ liệu form tạo user | MEDIUM | Redirect | FIXED | — | — | NOT_RUN | CLOSED |
| AUD-07 Menu theo quyền | LOW | Không kiểm tra quyền | FIXED | — | — | NOT_RUN | CLOSED |
| AUD-08 Badge trạng thái | LOW | Sai trường | FIXED | — | — | NOT_RUN | CLOSED |
| AUD-09 Contract đa agent | MEDIUM | Chỉ ghi Codex | FIXED (validator) | — | — | — | CLOSED |
| AUD-10 Encoding tiêu đề | LOW | `—` thành `?` | FIXED | — | — | — | CLOSED |
| AUD-11 Thứ tự manifest | MEDIUM | Chưa chốt thứ tự | FIXED | — | — | — | CLOSED |
| AUD-12 Quản lý vai trò | MEDIUM | Chưa có | FIXED, VERIFIED_BY_TEST | Khóa SQL NOT_VERIFIED | — | NOT_RUN | FIXED_IN_CODE; ma trận quyền/tạo role PENDING_APPROVAL |
| AUD-13 Throttle Admin | MEDIUM | Chưa có | FIXED, VERIFIED_BY_TEST + smoke | — | — | NOT_RUN | CLOSED (giới hạn xem AUD-24) |
| AUD-14 No-store | LOW | Chưa có | FIXED, VERIFIED_BY_TEST + smoke | — | — | NOT_RUN | CLOSED |
| AUD-15 Trạng thái tài khoản | MEDIUM | Luồng sửa | FIXED, VERIFIED_BY_TEST | NOT_VERIFIED | — | NOT_RUN | FIXED_IN_CODE; ý nghĩa Status khác ACTIVE PENDING_APPROVAL |
| AUD-16 MST | MEDIUM | Không kiểm tra trùng | FIXED (app) | Migration 009 NOT_EXECUTED | — | NOT_RUN | PENDING_OPERATOR |
| AUD-17 Xóa mềm | MEDIUM | Chưa có | User FIXED | NOT_VERIFIED | — | NOT_RUN | User FIXED_IN_CODE; doanh nghiệp PENDING_APPROVAL |
| AUD-18 Dashboard | LOW | Placeholder | FIXED, VERIFIED_BY_TEST | Tổng hợp SQL NOT_VERIFIED | — | NOT_RUN | FIXED_IN_CODE |
| AUD-19 Cảnh báo SYS_ADMIN khi tạo | LOW | Thiết kế | FIXED | — | — | NOT_RUN | CLOSED |
| AUD-20 Dummy hash | MEDIUM | Bỏ qua PBKDF2 | FIXED, VERIFIED_BY_TEST | — | — | — | CLOSED |
| AUD-21 Gợi ý username admin | LOW | Ghi chú sót | FIXED + smoke | — | — | — | CLOSED |
| AUD-22 AccessDeniedPath 404 | LOW | Sai route | FIXED + test tái hiện | — | — | — | CLOSED |
| AUD-23 Timing đăng nhập | LOW | Tải quyền trước verify | FIXED, VERIFIED_BY_TEST | — | — | TIMING_NOT_VERIFIED | FIXED_IN_CODE |
| AUD-24 Throttle phân tán/proxy | MEDIUM | Bộ nhớ từng process | PARTIAL (giới hạn bộ nhớ, allowlist proxy) | — | PENDING_INFRASTRUCTURE_APPROVAL, PROXY_NOT_VERIFIED | NOT_RUN | PENDING_APPROVAL |
| AUD-25 AuditLogs schema/quyền | MEDIUM | Chưa xác minh | Script 007 sẵn sàng | NOT_VERIFIED | — | NOT_RUN | PENDING_OPERATOR |
| AUD-26 Quyền trực tiếp | LOW | Không hiển thị | FIXED (chỉ đọc) | NOT_VERIFIED | — | NOT_RUN | FIXED_IN_CODE; sửa trực tiếp PENDING_APPROVAL |
| AUD-27 Race mất SYS_ADMIN | MEDIUM | Không transaction | FIXED (serializable) | Khóa SQL NOT_VERIFIED | — | NOT_RUN | FIXED_IN_CODE |
| AUD-28 Ghi đè đồng thời | LOW | Không stamp | FIXED | NOT_VERIFIED | — | NOT_RUN | FIXED_IN_CODE |
| AUD-29 Audit không bền vững | MEDIUM | Bỏ qua/500 | FIXED (fail-closed khi bật) | NOT_VERIFIED | CONFIGURATION_PENDING | NOT_RUN | PENDING_OPERATOR |
| AUD-30 Toggler mobile | LOW | Thiếu nút | FIXED (markup test) | — | — | BROWSER_NOT_VERIFIED | FIXED_IN_CODE |
| AUD-31 AccessDenied vòng lặp | LOW | Link cố định | FIXED, VERIFIED_BY_TEST | — | — | NOT_RUN | CLOSED |
| AUD-32 Hiệu lực membership | LOW | Chỉ xét cờ membership | FIXED, VERIFIED_BY_TEST | — | — | NOT_RUN | CLOSED |
| AUD-33 Link xóa/ghi chú SYS_ADMIN | LOW | UX | FIXED, VERIFIED_BY_TEST | — | — | NOT_RUN | CLOSED |
| AUD-34 Trợ năng | LOW | Thiếu aria | FIXED (markup test) | — | — | BROWSER_NOT_VERIFIED | FIXED_IN_CODE |
| AUD-35 Audit tạo user/đổi mật khẩu | MEDIUM | Thiếu audit | FIXED | NOT_VERIFIED | — | NOT_RUN | FIXED_IN_CODE |
| AUD-36 Audit membership | MEDIUM | Thiếu audit | FIXED | NOT_VERIFIED | — | NOT_RUN | FIXED_IN_CODE |
| AUD-37 Audit doanh nghiệp | LOW | Thiếu audit | FIXED, VERIFIED_BY_TEST | NOT_VERIFIED | — | NOT_RUN | FIXED_IN_CODE |
| AUD-38 Timing Customer Portal | LOW | Tải membership trước verify | FIXED, VERIFIED_BY_TEST | — | — | — | CLOSED |
| **AUD-39** Security headers (mới CP10) | MEDIUM | Không có X-Frame-Options/nosniff/Referrer-Policy/frame-ancestors | FIXED, VERIFIED_BY_TEST + smoke | — | IIS có thể thêm header riêng (IIS_NOT_VERIFIED) | NOT_RUN | CLOSED |
| **AUD-40** Guard script 003 (mới CP10) | LOW | Script chỉ đọc không guard | FIXED | NOT_EXECUTED | — | — | CLOSED |
| **AUD-41** Script MCP reader dùng DB `TAPortal` (mới CP10) | MEDIUM (vận hành) | `scripts/001`, `002` có `USE [TAPortal]` | Không sửa (tạo login — phạm vi DBA) | NOT_VERIFIED | MCP chỉ đọc không kết nối được | — | PENDING_OPERATOR |
| AUD-42 Audit đổi mật khẩu Customer Portal (ghi nhận CP9) | LOW | Chưa có cơ chế ở app Customer | — | — | — | — | PENDING_APPROVAL |
| CPF Migration 008 | — | — | — | OPERATOR_REPORTED (CP3), chưa có bằng chứng agent | — | — | NOT_VERIFIED |
| CPF UAT-01…09 | — | — | — | — | — | NOT_RUN | PENDING_OPERATOR |

Tổng: 42 finding AUD (AUD-01…42) + 2 mục Customer Portal foundation; **0 CRITICAL**, 2 HIGH (đều FIXED_IN_CODE). Phân bố Final status (AUD-01…42): CLOSED 21, FIXED_IN_CODE 15, PENDING_OPERATOR 4 (AUD-16/25/29/41), PENDING_APPROVAL 2 (AUD-24/42). Mục foundation: migration 008 NOT_VERIFIED, UAT-01…09 PENDING_OPERATOR. Không còn lỗi code nghiêm trọng chưa ghi nhận.

## 2. Hồi quy chức năng (safe tests, không SQL)
- Admin: đăng nhập/đăng xuất/thu hồi phiên (SafeAdminSecurity, Hardening), CRUD user + trạng thái + vai trò + SYS_ADMIN cuối + xóa mềm (Crud, Account, Role), doanh nghiệp + MST (Account), membership hiển thị (DashboardUx), dashboard KPI/health (DashboardUx), navigation/authorization/CSRF/AccessDenied/no-store (Crud, Security, Release). Render các trang dùng `PortalDb` trực tiếp (danh sách Users/Customers/Roles/Menus, Profile, CustomerPortalAccounts) chỉ kiểm tra qua route/authorization — DATABASE_NOT_VERIFIED.
- Customer Portal (SafePortalTests 36): login/logout, mật khẩu, trạng thái tài khoản, membership, chọn/chuyển doanh nghiệp, thời hạn cookie cố định, cô lập tenant, hồ sơ, đổi mật khẩu, dashboard, thu hồi, tách cookie, throttle, AUD-38.

## 3. Hồi quy bảo mật
Account enumeration/dummy PBKDF2 (AUD-20/23/38), thông báo lỗi chung, throttle + Retry-After + giới hạn bộ nhớ, forwarded headers allowlist, giới hạn multi-instance (test ghi nhận), xác thực lại cookie, authorization + kiểm kê endpoint (mới), quyền trực tiếp chỉ đọc, tenant isolation, CSRF, output encoding (test `<script>`), SQL tham số hóa, log không chứa username/mật khẩu, audit fail-closed (bảng quyết định), thu hồi tài khoản/membership, xóa mềm, concurrency/stamp, xử lý lỗi không lộ chi tiết, security headers (mới), no-store.

## 4. Kiểm thử theo lớp
| Lớp | Số test | Phạm vi |
|---|---|---|
| SafePortalTests | 36 | Customer Portal |
| SafeAdminCrudTests | 31 | Admin CRUD/authz/CSRF |
| SafeAdminSecurityTests | 19 | AUD-13/14/20–22 |
| SafeAdminRoleTests | 20 | AUD-12 |
| SafeAdminAccountTests | 32 | AUD-15/16/17/27/28 |
| SafeAdminDashboardUxTests | 24 | AUD-18/26/30–34 |
| SafeAdminHardeningTests | 40 | AUD-23/24/29/35–37 |
| SafeReleaseRegressionTests | 13 | AUD-39, kiểm kê endpoint |
| **Tổng** | **215** | 215 PASS / 0 FAIL / 0 SKIP |
Chưa có test: câu SQL thực thi trên SQL Server (cấm integration test ghi DB), trình duyệt thật, proxy/IIS thật, đo timing.

## 5. Quyết định release
| Khía cạnh | Kết luận | Lý do |
|---|---|---|
| Code release candidate | **READY** | Build 0/0, 215 test, Codec PASS, cặp artifact cùng SHA, smoke 16/16 + 10/10, không CRITICAL/HIGH mở |
| Deployment readiness | **CONDITIONAL GO** | Cần hoàn tất Phase A (backup, xác minh 004/007 trên TAPortalSys, quyền SQL, secrets, Data Protection, HTTPS, AllowedHosts, quyết định proxy) |
| Production readiness | **NOT READY** | UAT (C1–C20, UAT-01…09) chưa chạy; DB chưa xác minh; audit bền vững chưa bật |
