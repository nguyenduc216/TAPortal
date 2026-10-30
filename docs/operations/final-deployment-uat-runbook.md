# TAPortal — Runbook triển khai & UAT tổng thể (Admin + Customer Portal)

- Release candidate: source SHA `95e64cedb14479850bc3f32d28ec0aa6c36f7d02` (CHANGE_ID `TAPORTAL-SYSTEM-AUDIT-CHG-20261029T052755Z-8ECA9D`)
- Artifact Admin: `D:\T.A\TAPortal\TAPortal\publish\deployed-TAPortal.Web` — 57 file — SHA256 manifest `6cfdc285c736386f8e1b6a94303286a07cc0bbf0c912d685aa4db122e5edfe69`
- Artifact Customer: `D:\T.A\TAPortal\TAPortal\publish\deployed-TACustomerPortal.Web` — 54 file — SHA256 manifest `cb152b0567270cb4fe64d8b2aa01dd20007fac1c30ec4b7c7b4403c35cfe3efc`
- `TAPortal.Shared.dll` giống nhau ở hai artifact: `dca4b1ade8e096015bfab1f2970250a660ecbea8fce2acfa57d1633326384a65`
- Manifest: `.codec/reports/TAPORTAL-SYSTEM-AUDIT-CHG-20261029T052755Z-8ECA9D-<Project>.sha256`. Kiểm tra: `sha256sum <manifest>` = SHA256 trên; trong thư mục artifact `sha256sum -c <manifest>` PASS.
- Database duy nhất: `TAPortalSys` trên `WIN-HSHL54Q184K\MSSQLSERVER2014`. **Mọi bước dưới đây do operator/DBA thực hiện; AI agent không thực hiện bước nào.**

## Phase A — Trước triển khai

| # | Bước | Ai | Bắt buộc | Bằng chứng cần lưu |
|---|---|---|---|---|
| A1 | Phê duyệt: phạm vi release (PR #7 chưa merge), cửa sổ triển khai, người chịu trách nhiệm rollback | Operator | Có | Biên bản phê duyệt |
| A2 | Backup đầy đủ TAPortalSys (và kiểm tra restore được) | DBA | Có | Tên file backup, thời điểm |
| A3 | Xác minh schema membership: `database/scripts/003-customer-portal-schema-audit.sql` rồi `004-verify-customer-portal.sql` (chỉ đọc). Migration 008 hiện chỉ có **báo cáo của operator** (CP3: PASS_WITH_WARNINGS, 0 membership) — cần chạy lại 004 để có bằng chứng mới. **Không chạy lại 008 nếu 004 PASS.** | DBA | Có | Output Messages, `VERIFY_STATUS` |
| A4 | Xác minh AuditLogs: `database/scripts/007-verify-audit-logs.sql` với `@AppPrincipal` = DB user của Admin | DBA | Có | `VERIFY_STATUS` |
| A5 | Nếu A4 xác nhận (bằng db_owner) bảng thật sự thiếu: duyệt rồi chạy `database/migrations/010-create-audit-logs-if-missing.sql`; chạy lại A4 | DBA | Điều kiện | Output |
| A6 | Quyền SQL tối thiểu: Admin app — SELECT/INSERT/UPDATE Users, SELECT/INSERT/DELETE UserRoles, SELECT Roles/Permissions/RolePermissions/UserPermissions/Menus, SELECT/INSERT/UPDATE Customers, SELECT/INSERT/UPDATE CustomerPortalMemberships, INSERT AuditLogs (template `scripts/009-grant-admin-audit-insert-template.sql`). Customer app — theo `scripts/006-customer-portal-least-privilege.sql` (không quyền ghi Customers/memberships). Không dùng db_owner, không dùng user DbMcp | DBA | Có | Danh sách GRANT đã áp dụng |
| A7 | MST trùng: `database/scripts/008-audit-customer-taxcode-duplicates.sql`. Nếu PASS và đồng ý ràng buộc DB → chạy `migrations/009-customer-taxcode-unique-index.sql` (rollback: `scripts/010-…`) | DBA | Khuyến nghị | Output |
| A8 | Topology reverse proxy: có/không, IP chính xác của proxy, proxy có ghi đè X-Forwarded-For; giới hạn đăng nhập dùng chung tại gateway/WAF nếu nhiều instance hoặc có proxy | Hạ tầng | Có (quyết định) | Sơ đồ, IP |
| A9 | Cấu hình môi trường theo bảng "Cấu hình" bên dưới (ngoài Git, không đưa secret vào tài liệu) | Operator | Có | Checklist cấu hình |
| A10 | HTTPS: chứng chỉ hợp lệ cho cả hai hostname; cookie là Secure ngoài Development nên **bắt buộc HTTPS** | Hạ tầng | Có | Hostname, ngày hết hạn chứng chỉ |
| A11 | Data Protection: thư mục key bền vững, riêng cho từng app, ngoài web root, ACL chỉ cho app pool identity, Load User Profile bật (DPAPI) | Operator | Có | Đường dẫn, ACL |
| A12 | Backup bản đang chạy của cả hai site (thư mục + cấu hình) | Operator | Có | Vị trí backup |
| A13 | Không chạy: migrations 001–007, scripts 001–002 (dùng `USE [TAPortal]`, không phải TAPortalSys), script 005 (rollback membership) | DBA | — | — |

### Cấu hình
| Setting | Ứng dụng | Mặc định hiện tại | Khuyến nghị | Phụ thuộc | Hành động operator |
|---|---|---|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | Cả hai | (Production nếu không đặt) | `Production` | — | Đặt tường minh |
| `Database__ConnectionString` | Cả hai | rỗng (khởi động lỗi ngay) | Chuỗi tới TAPortalSys, principal riêng từng app | A6 | Đặt bằng biến môi trường/secret store |
| `DataProtection__KeyPath` | Cả hai | rỗng (key không bền vững) | Thư mục riêng từng app | A11 | Đặt, cấp ACL |
| `AllowedHosts` | Cả hai | `*` | Hostname thật, phân tách `;` | DNS | Đặt |
| `Security__Audit__RequireDurableAudit` | Admin | `false` (log `DURABLE_AUDIT_NOT_ENFORCED`) | `true` | A4–A6 PASS | Bật sau khi xác minh (runbook audit bước 7) |
| `Security__AdminLogin__AccountPermitLimit` / `IpPermitLimit` / `Window` / `MaxTrackedKeys` | Admin | 5 / 20 / `00:05:00` / 100000 | Giữ hoặc phê duyệt giá trị khác | A8 | Xác nhận |
| `ReverseProxy__KnownProxies__N` | Cả hai | rỗng (bỏ qua X-Forwarded-*) | IP chính xác từng proxy (không dải, không `*`) | A8 | Chỉ đặt khi có proxy |
| `ASPNETCORE_HTTPS_PORT` | Cả hai | — | 443 nếu TLS kết thúc ở IIS/proxy mà app không thấy cổng HTTPS | A10 | Đặt nếu cần |
| Logging (stdout/ETW/central) | Cả hai | Console provider; web.config theo publish | Đích log có retention, không ghi stdout vô hạn | — | Cấu hình |
| Cookie | Admin: `TA.Portal.Auth` 8h trượt; Customer: `TA.Customer.Auth` 2h cố định | — | Giữ nguyên | A10 | Không |

## Phase B — Triển khai (chỉ hướng dẫn)
1. Dừng site Customer Portal, rồi Admin (hoặc dùng app_offline.htm).
2. Xác minh SHA256 manifest của hai artifact (mục đầu tài liệu) trước khi chép.
3. Chép toàn bộ nội dung `deployed-TAPortal.Web` vào thư mục site Admin; `deployed-TACustomerPortal.Web` vào site Customer. Không chép thư mục `publish\rollback`.
4. Đặt cấu hình môi trường/secret theo bảng trên cho từng site (không sửa appsettings.json trong artifact để đưa secret).
5. Tạm để `RequireDurableAudit=false` nếu A4 chưa PASS; nếu đã PASS thì đặt `true`.
6. Khởi động Admin: log không có lỗi khởi động; nếu cờ audit tắt sẽ có cảnh báo `DURABLE_AUDIT_NOT_ENFORCED` (đã biết).
7. Khởi động Customer Portal.
8. Kiểm tra truy cập DB: Admin → Tổng quan hiển thị "Đọc được dữ liệu" và số liệu; Customer Portal → trang đăng nhập 200.
9. Kiểm tra xác thực: đăng nhập SYS_ADMIN test; đăng nhập Customer Portal bằng tài khoản test.
10. Kiểm tra header: response có `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, trang động có `Cache-Control: no-store`.

## Phase C — UAT
Đánh dấu PASS/FAIL, lưu bằng chứng (ảnh/HTTP log không chứa secret). Tài khoản test do operator tạo; không dùng tài khoản thật của khách hàng.

| # | Vai trò/tình huống | Bước | Kỳ vọng |
|---|---|---|---|
| C1 | SYS_ADMIN | Đăng nhập; mở Tổng quan, Người dùng, Khách hàng, Tài khoản Customer Portal, Vai trò | Truy cập được; KPI khớp truy vấn chỉ đọc |
| C2 | Non-admin (có vai trò không phải SYS_ADMIN) | Mở `/users` | Trang "Không có quyền", nút "Về hồ sơ cá nhân" |
| C3 | Customer Portal OWNER/ADMIN/VIEWER | Đăng nhập Customer Portal | Chỉ thấy doanh nghiệp được cấp; không vào được Admin |
| C4 | Nhiều doanh nghiệp | Chọn/chuyển doanh nghiệp | Hồ sơ đúng doanh nghiệp; phiên không gia hạn quá 2h |
| C5 | User bị vô hiệu hóa | Tắt "Cho phép đăng nhập" | Không đăng nhập được ở cả hai app; phiên đang mở bị đăng xuất |
| C6 | User đã xóa mềm | Xóa user test | Không đăng nhập được; dòng DB còn với IsDeleted=1; trang sửa trả 404 |
| C7 | User bị khóa (Status≠ACTIVE, do DBA đặt trên tài khoản test) | Đăng nhập | Thông báo chung, không lộ trạng thái |
| C8 | Doanh nghiệp ngừng hoạt động | Tắt hoạt động | Membership "Không hiệu lực" + lý do; Customer Portal không truy cập được |
| C9 | Membership bị thu hồi | Thu hồi | Phiên Customer Portal bị đăng xuất ở request kế tiếp |
| C10 | MST trùng | Tạo doanh nghiệp với MST đã có (thêm khoảng trắng) | Bị chặn, nêu mã KH trùng; `…-001` chi nhánh được chấp nhận |
| C11 | Sửa user đồng thời | Hai tab cùng sửa, lưu lần lượt | Tab sau báo xung đột, dữ liệu không bị ghi đè |
| C12 | SYS_ADMIN cuối | Gỡ vai trò/vô hiệu hóa/xóa SYS_ADMIN cuối | Đều bị chặn |
| C13 | Dashboard | Đối chiếu KPI với truy vấn chỉ đọc (định nghĩa trong báo cáo CP8) | Khớp |
| C14 | Audit trail | Thực hiện từng thao tác trong runbook audit | Có dòng tương ứng trong dbo.AuditLogs |
| C15 | Audit failure rollback (môi trường test) | Theo runbook audit bước 8 | Thay đổi bị hủy, thông báo lỗi audit |
| C16 | Throttle | 6 lần đăng nhập sai | Lần 6 → 429 + Retry-After; hồi phục sau 5 phút |
| C17 | Reverse proxy (nếu có) | Đăng nhập sai từ 2 client sau proxy | Bộ đếm IP tách theo client thật; header giả từ client không có tác dụng |
| C18 | Mobile/tablet | Mở Admin và Customer Portal trên điện thoại/máy tính bảng | Nút menu mở/đóng, bảng cuộn ngang, form dùng được |
| C19 | Đăng xuất/Back | Đăng xuất rồi bấm Back | Không thao tác được dữ liệu; mọi click về trang đăng nhập |
| C20 | Đổi mật khẩu | Đổi ở một app | Phiên cũ ở cả hai app bị vô hiệu |

Thêm UAT-01…09 của Customer Portal: `docs/customer-portal-checkpoint4-uat.md`.

## Phase D — Rollback
**Kích hoạt khi:** lỗi khởi động; đăng nhập SYS_ADMIN không được; rò rỉ dữ liệu giữa doanh nghiệp; lỗi audit chặn mọi thao tác ngoài dự kiến; regression nghiêm trọng trong UAT.
1. Ứng dụng: dừng site, khôi phục thư mục site đã backup ở A12 (hoặc dùng artifact trước trong `publish\rollback\TAPORTAL-SYSTEM-AUDIT-CHG-20261029T052755Z-8ECA9D\`), khởi động.
2. Cấu hình: khôi phục biến môi trường đã backup; nếu chỉ lỗi audit → đặt `Security__Audit__RequireDurableAudit=false` và khởi động lại.
3. Database: **không rollback schema mặc định.** Migration 009 chỉ gỡ bằng `scripts/010-rollback-customer-taxcode-unique-index.sql` khi có phê duyệt. Không xóa bảng AuditLogs/CustomerPortalMemberships có dữ liệu; script 005 từ chối khi có membership.
4. Audit: giữ nguyên mọi dòng dbo.AuditLogs (không xóa/sửa khi rollback).
5. Sau rollback: đăng nhập SYS_ADMIN, Customer Portal login 200, kiểm tra header và log; ghi biên bản.
