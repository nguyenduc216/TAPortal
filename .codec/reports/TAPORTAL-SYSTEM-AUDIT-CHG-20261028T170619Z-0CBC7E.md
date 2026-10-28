# TAPORTAL CHECKPOINT 5 — SECURITY DELIVERY REPORT

Feature ID: `TAPORTAL-SYSTEM-AUDIT` · Change ID: `TAPORTAL-SYSTEM-AUDIT-CHG-20261028T170619Z-0CBC7E` · Agent: Claude Code (claude-opus-5-5)

## A. Identification
- FEATURE_ID: TAPORTAL-SYSTEM-AUDIT (checkpoint CHECKPOINT-05-ADMIN-SECURITY)
- CHANGE_ID: TAPORTAL-SYSTEM-AUDIT-CHG-20261028T170619Z-0CBC7E
- Branch: feature/customer-portal-mvp01
- Starting SHA: 1e1dda00255dd9fefa4db18ada8076f406e2bb68 (bằng origin; không có commit nào từ agent khác)
- Source SHA: 183414102691ffb373d436951b27cf191d902fc2
- Delivery SHA: commit chứa báo cáo này — báo sau khi push
- PR URL: https://github.com/nguyenduc216/TAPortal/pull/7
- Merge status: NOT_MERGED

## B. Security Audit
- AUD-13 root cause: Admin không có bất kỳ giới hạn nào cho POST /account/login; không lockout, không log thất bại.
- AUD-14 root cause: không có chính sách Cache-Control cho response động; chỉ trang login có no-store (nhờ antiforgery).
- Phát hiện thêm: AUD-20 dò username qua thời gian (MEDIUM, FIXED), AUD-21 trang login công khai username `admin` (LOW, FIXED), AUD-22 AccessDeniedPath trả 404 (LOW, FIXED), AUD-23 chênh lệch thời gian nhỏ còn lại (LOW, OPEN), AUD-24 bộ đếm theo từng process / IP proxy dùng chung (MEDIUM, OPEN/PENDING_OPERATOR).
- Affected files: src/TAPortal.Web/Program.cs, Controllers/AccountController.cs, Security/AdminLoginThrottle.cs (mới), Views/Account/Login.cshtml.
- Risk assessment: trước khi sửa, brute-force online không bị giới hạn, chỉ bị chậm bởi PBKDF2; có thể liệt kê username; trang quản trị có thể bị lưu trên máy dùng chung. Sau khi sửa: còn AUD-23/24.
- Solution: throttle hai partition trong bộ nhớ + dummy hash + middleware no-store tập trung. Chi tiết: docs/audits/taportal-checkpoint5-admin-security-TAPORTAL-SYSTEM-AUDIT-CHG-20261028T170619Z-0CBC7E.md

## C. AUD-13 Implementation
- Algorithm: fixed window cho từng partition, trong bộ nhớ, dựa trên `TimeProvider` (tự triển khai; lý do không dùng `AddRateLimiter`: .NET 8 không cho inject đồng hồ để test phục hồi tất định, và partition tài khoản cần dữ liệu form đã bind).
- Thresholds (đề xuất, cấu hình được): tài khoản 5 / 5 phút; IP 20 / 5 phút; `Security__AdminLogin__AccountPermitLimit|IpPermitLimit|Window`, giá trị không hợp lệ làm ứng dụng dừng khi khởi động.
- Partition: SHA-256(username trim + viết hoa), độc lập IP; SHA-256(IP socket). Không tin X-Forwarded-For. Không lưu dữ liệu thô.
- HTTP 429: trang login với thông báo chung, không kiểm tra thông tin đăng nhập, hành vi giống nhau cho tài khoản có/không tồn tại; response có no-store.
- Retry-After: số giây còn lại của cửa sổ đang chặn (300 ở lần chặn đầu).
- Failure handling: antiforgery chạy trước (400, không bị đếm); throttle trước validation, tra cứu và hash; lần bị từ chối không kéo dài cửa sổ; tự phục hồi, không khóa vĩnh viễn; log Warning khi bị chặn và Information khi đăng nhập thất bại, chỉ ghi tag hash 12 ký tự.
- Limitations: giới hạn theo số lần thử (request), không phải lockout theo số lần sai; reset khi app pool recycle; không chia sẻ giữa instance; sau proxy, giới hạn IP thành giới hạn chung (AUD-24); có thể làm chậm một tài khoản đã biết tối đa 5 phút cho mỗi cửa sổ.

## D. AUD-14 Implementation
- Protected routes: mọi response động của Admin (trang đã xác thực, login, 302 tới login, 403, 429, lỗi, logout) qua một middleware đặt sau `UseStaticFiles`.
- Headers: `Cache-Control: no-store, no-cache, max-age=0`, `Pragma: no-cache`, `Expires: 0`; không có `public`.
- Logout/session: xóa cookie `TA.Portal.Auth` và gửi `Clear-Site-Data: "cache"`; phiên vẫn được kiểm tra lại với DB mỗi request (không đổi).
- Static assets: CSS/JS/images giữ ETag, không có no-store (đã kiểm tra bằng test và smoke).
- Limitations: back/forward cache của trình duyệt có thể hiển thị lại trang trong bộ nhớ (không thao tác được); Clear-Site-Data chưa được mọi trình duyệt hỗ trợ đầy đủ; header phía trình duyệt cần UAT.

## E. Tests
- Release build: PASS — 0 warnings, 0 errors
- Existing safe tests: 65/65 PASS (SafePortalTests 34, SafeAdminCrudTests 31)
- New security tests: SafeAdminSecurityTests 19/19 PASS (đồng hồ giả lập, IP qua startup filter chỉ dùng trong test, không SQL)
- Total: 84 passed / 0 failed / 0 skipped; filter `FullyQualifiedName~TACustomerPortal.Tests.Safe`; legacy SQL tests not selected
- AUD-22 có test tái hiện lỗi (404) trước khi sửa, PASS sau khi sửa
- Codec tests: 4/4 (exit 0); Codec validation: PASS (exit 0)
- Smoke artifact Admin (Production, Kestrel, SQL cố ý sai): 11/11 PASS

## F. Changed Files
| File | Lý do | AUD |
|---|---|---|
| src/TAPortal.Web/Security/AdminLoginThrottle.cs (mới) | Throttle hai partition | AUD-13 |
| src/TAPortal.Web/Controllers/AccountController.cs | Throttle, dummy hash, log, Clear-Site-Data | AUD-13, 20, 14 |
| src/TAPortal.Web/Program.cs | DI/cấu hình throttle, middleware no-store, AccessDeniedPath | AUD-13, 14, 22 |
| src/TAPortal.Web/Views/Account/Login.cshtml | Xóa gợi ý username admin | AUD-21 |
| tests/TACustomerPortal.Tests/SafeAdminSecurityTests.cs (mới) | 19 regression test | AUD-13, 14, 20, 21, 22 |
| AGENTS.md, .agents/skills/codec/SKILL.md | Filter safe test chung | Contract |
| docs/customer-portal-runbook.md | Lệnh test, cấu hình, giới hạn | AUD-13, 14 |
| docs/audits/taportal-checkpoint5-admin-security-…md, .codec/* | Báo cáo, lịch sử, manifest | — |

## G. Database
- Access status: DATABASE_NOT_VERIFIED (không truy cập)
- Schema changes: none
- Migration files: none
- Migration execution status: NONE_REQUIRED / NOT_EXECUTED
- Operator action required: none

## H. Publish Admin
- Status: PUBLISHED (local) / NOT_DEPLOYED
- Source SHA: 183414102691ffb373d436951b27cf191d902fc2
- Target: D:\T.A\TAPortal\TAPortal\publish\deployed-TAPortal.Web
- Publish timestamp: 2026-10-28T17:12:30Z → 17:12:32Z
- Exit code: 0
- File count: 57 (DLL, deps.json, runtimeconfig.json, web.config [parse OK], wwwroot 7 file, 0 dependency thiếu); TAPortal.Web.dll SHA256 c64ce149392fa0a9d6e8786cc9efdcb5682a7331621ce5cca849aef34be973f4
- Manifest path: .codec/reports/TAPORTAL-SYSTEM-AUDIT-CHG-20261028T170619Z-0CBC7E-TAPortal.Web.sha256
- Artifact SHA256: 42d6a7334a744094e4a8135463801a477b93fe86dd5e811c93cedf311e119cb4
- Bản trước: 4b308e3bfbfddf80cffaf1fd099981d07b597128af1d5ab03e31ebca8b05ea35, được xác minh rồi backup tại publish\rollback\TAPORTAL-SYSTEM-AUDIT-CHG-20261028T170619Z-0CBC7E\deployed-TAPortal.Web

## I. Publish Customer
- Status: NOT_REQUIRED
- Reason: không thay đổi src/TACustomerPortal.Web hay src/TAPortal.Shared (kiểm tra bằng git status); Customer Portal vẫn PASS 34 test
- Artifact hiện tại giữ nguyên, đã kiểm tra lại: ea77b3be501ea28fe80ce54665a805b6798b502137347ce11f7d356547f8ac34 (source 336ab7c)

## J. Security Acceptance
- AUD-13: FIXED (automated + smoke); UAT trình duyệt PENDING_OPERATOR
- AUD-14: FIXED (automated + smoke); UAT trình duyệt PENDING_OPERATOR
- Remaining risks: AUD-23 (LOW), AUD-24 (MEDIUM, cần gateway/WAF và quyết định trusted proxy), AUD-12 ngoài phạm vi
- Manual UAT checklist: mục 6 của báo cáo audit Checkpoint 5
- READY_FOR_UAT

## K. Git Delivery
- Source commit SHA: 183414102691ffb373d436951b27cf191d902fc2
- Delivery commit SHA / push result / remote HEAD / PR state: báo trong thông báo bàn giao
- Không commit: database/scripts/004-verify-customer-portal.sql, database/scripts/scene-image.png (của operator), src/.claude/, publish/, TestResults/
- Merge status: NOT_MERGED

## L. Operator Handoff
- Exact artifact path: D:\T.A\TAPortal\TAPortal\publish\deployed-TAPortal.Web (Customer không đổi)
- Files to deploy: toàn bộ 57 file của thư mục Admin; xác minh lại SHA256 bằng manifest trước khi chép (`sha256sum` manifest = artifact SHA256; `sha256sum -c` trong thư mục artifact)
- Required configuration changes: không bắt buộc. Tùy chọn `Security__AdminLogin__*`. Nếu chạy sau proxy, cần gateway/WAF limit (AUD-24).
- Backup/rollback: bản Admin trước tại publish\rollback\…\deployed-TAPortal.Web; trên server, operator backup site trước khi thay; rollback source `git revert 1834141`.
- Manual verification steps: checklist UAT 1–6 trong báo cáo audit Checkpoint 5.
- Next recommended checkpoint: operator UAT (Checkpoint 4B + Admin CRUD + Checkpoint 5) trên trình duyệt; sau đó quyết định AUD-24 (gateway/proxy) và AUD-12 (quy tắc quản lý vai trò).
