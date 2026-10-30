# Checkpoint 9 — Security hardening (AUD-23, AUD-24, AUD-25, AUD-29) + regression

- CHANGE_ID `TAPORTAL-SYSTEM-AUDIT-CHG-20261029T050717Z-E1EAC3`; starting SHA `8fea21674aa8dd24a065bbd6204117ea6669d2f2`; source SHA `b6eb780202049b9a8ca3014e28d50cfcd3e2e8dd`
- Agent: Claude Code (Claude Opus 5.5). DATABASE_NOT_VERIFIED, PROXY_NOT_VERIFIED, TIMING_NOT_VERIFIED, BROWSER_NOT_VERIFIED.

## 1. Bảng findings

| ID | Severity | Root cause | Evidence (tại 8fea216) | Fix | Test | Status |
|---|---|---|---|---|---|---|
| AUD-23 | LOW | Admin login tải vai trò/quyền (3 truy vấn) trước khi verify mật khẩu; `Verify` bỏ qua PBKDF2 khi hash rỗng/sai định dạng | `PortalDb.GetAuthUserAsync`, `AccountController.Login` | `IAdminAuthStore`: tìm user (1 truy vấn) → PBKDF2 (dummy cho cả hash rỗng/sai) → chỉ tải quyền khi mật khẩu đúng | 6 trường hợp thất bại cùng chuỗi gọi `[find]` và cùng HTML; mật khẩu đúng → `[find, grants, touch]` | FIXED (TIMING_NOT_VERIFIED) |
| AUD-38 (mới) | LOW | Customer Portal tải membership trước khi xác nhận mật khẩu; cùng lỗi hash rỗng | `TACustomerPortal.Web/Controllers/AccountController.cs` | Chỉ tải membership khi mật khẩu đúng; dummy PBKDF2 | 2 test SafePortalTests | FIXED |
| AUD-24 | MEDIUM | Bộ đếm không giới hạn số key; không có cấu hình trusted proxy; theo từng process | `AdminLoginThrottle`, `Program.cs` | `MaxTrackedKeys` (fail-closed khi đầy, không xóa bộ đếm đang sống); allowlist IP proxy chính xác, mặc định tắt, bỏ trust loopback ngầm định, cấu hình sai dừng khởi động; `IAdminLoginThrottle` cho store dùng chung sau này | Concurrency 200 → 5; giới hạn bộ nhớ; 2 instance độc lập (giới hạn); parse allowlist; XFF chỉ từ proxy tin cậy | PARTIAL — PENDING_INFRASTRUCTURE_APPROVAL (store dùng chung/gateway); PROXY_NOT_VERIFIED |
| AUD-29 | MEDIUM | Bảng không nhìn thấy → bỏ qua audit; INSERT lỗi khi bảng nhìn thấy → 500 bất kể cấu hình | `AuditSql` (CP7) | `TAPortal.Shared.AuditSql`: kiểm tra OBJECT_ID → COL_LENGTH → INSERT qua `sp_executesql`; kết quả Written/TableUnavailable/SchemaMismatch/InsertFailed; `RequireDurableAudit=true` → rollback mọi trường hợp chưa ghi; transaction doomed luôn rollback; cảnh báo khởi động `DURABLE_AUDIT_NOT_ENFORCED` | Bảng quyết định 7 case; phân loại; opt-in; HTTP khách hàng 400 khi audit bắt buộc | FIXED trong code — CONFIGURATION_PENDING (operator bật cờ) |
| AUD-25 | MEDIUM | Chưa xác minh bảng/quyền trên TAPortalSys | — | Script 007: phân biệt "không nhìn thấy" và "không tồn tại" theo quyền người chạy; kiểm tra visibility + INSERT của principal ứng dụng, quyền membership; runbook bật audit | Đọc lại script (không chạy) | PARTIAL — PENDING_OPERATOR |
| AUD-35 (mới) | MEDIUM | Tạo user (có thể gán SYS_ADMIN) và đổi mật khẩu Admin không có audit | `PortalDb.CreateUserAsync`, `UpdatePasswordAsync` | Audit `USER_CREATED`, `USER_PASSWORD_CHANGED` (không lưu hash) trong transaction | DATABASE_NOT_VERIFIED | FIXED (code) |
| AUD-36 (mới) | MEDIUM | Cấp/thu hồi membership Customer Portal (có thể tạo user) không có audit | `MembershipAdminStore` (Shared) | Audit `CUSTOMER_PORTAL_MEMBERSHIP_SAVED/_ACTIVE_CHANGED` trong transaction | DATABASE_NOT_VERIFIED | FIXED (code) |
| AUD-37 (mới) | LOW | Tạo/sửa doanh nghiệp không có audit | `SqlCustomerAdminStore` | Audit `CUSTOMER_CREATED/UPDATED` | HTTP 400 khi audit bắt buộc | FIXED (code) |

## 2. Danh mục thao tác nhạy cảm và audit
| Thao tác | Audit trong transaction | Fail-closed khi RequireDurableAudit=true |
|---|---|---|
| Đổi vai trò user | Có (CP6) | Có |
| Sửa user / vô hiệu hóa | Có (CP7) | Có |
| Xóa mềm user | Có (CP7) | Có |
| Tạo user (kèm vai trò) | **Có (CP9)** | Có |
| Đổi mật khẩu Admin | **Có (CP9)** | Có |
| Tạo/sửa doanh nghiệp | **Có (CP9)** | Có |
| Cấp/đổi vai trò/thu hồi/kích hoạt membership Customer Portal | **Có (CP9)** | Có |
| Đổi mật khẩu trong Customer Portal | Không (ứng dụng khác, chưa có cơ chế) | PENDING_APPROVAL |
| Đăng nhập thành công/thất bại | Không (log ứng dụng, LastLoginAt) | Không áp dụng |

## 3. Giới hạn còn lại (không tuyên bố đã giải quyết)
- Timing: không đo thời gian trên môi trường đại diện; HTTP login không constant-time toàn phần (khác biệt mạng/DB vẫn có). Người có mật khẩu đúng nhưng không có quyền Admin sẽ tốn thêm 1 lượt tải quyền — chỉ quan sát được khi đã biết mật khẩu.
- Throttle: bộ nhớ từng process; restart/nhiều instance không chia sẻ; sau proxy chưa cấu hình, mọi client chung một partition IP. Khi đầy key (tấn công phân tán lớn) các partition mới bị từ chối tới khi cửa sổ hết hạn — cần WAF/gateway.
- Audit: chưa chạy trên TAPortalSys; `RequireDurableAudit` vẫn `false` (agent không tự bật).

## 4. Security regression
Giữ nguyên và có test: AUD-12/13/14/15/16/17/18/20/21/22/26/27/28/30–34; SYS_ADMIN kiểm tra lại từ DB; Customer Portal không có quyền Admin; tenant isolation; CSRF; tách cookie TA.Portal.Auth/TA.Customer.Auth; no-store; thu hồi tài khoản/membership; không SQL ghép chuỗi từ input (tham số hóa; `sp_executesql` có tham số); không lộ stack trace; log không chứa username/mật khẩu (test LogSink).
Test sửa có chủ đích: `SafeAdminRoleTests.RoleSqlNeverTouches…` cập nhật theo câu audit mới (vẫn yêu cầu guard OBJECT_ID đứng đầu).

## 5. Kiểm thử
- Mới: SafeAdminHardeningTests (40) + 2 test Customer Portal (AUD-38). Tổng 202 PASS / 0 FAIL / 0 SKIP.
- Smoke: Admin 15/15, Customer 10/10 (Production, SQL cố ý sai).

## 6. Operator / hạ tầng
- `docs/operations/durable-audit-enablement-runbook.md` (8 bước: backup → verify schema → verify principal → migration 010 nếu thật sự thiếu → GRANT INSERT → verify lại → bật cờ → kiểm tra rollback).
- Proxy: chỉ khi có reverse proxy, đặt `ReverseProxy__KnownProxies__N` = IP chính xác của proxy; proxy phải ghi đè X-Forwarded-For; giới hạn đăng nhập dùng chung tại gateway/WAF (PENDING_INFRASTRUCTURE_APPROVAL).
