# TAPORTAL CHECKPOINT 9 — SECURITY HARDENING DELIVERY REPORT

Feature ID: `TAPORTAL-SYSTEM-AUDIT` · Change ID: `TAPORTAL-SYSTEM-AUDIT-CHG-20261029T050717Z-E1EAC3` · Agent: Claude Code (claude-opus-5-5)

## A. Identification
- FEATURE_ID TAPORTAL-SYSTEM-AUDIT; CHANGE_ID TAPORTAL-SYSTEM-AUDIT-CHG-20261029T050717Z-E1EAC3; branch feature/customer-portal-mvp01
- Starting SHA 8fea21674aa8dd24a065bbd6204117ea6669d2f2 (bằng origin; không có commit agent khác)
- Source SHA b6eb780202049b9a8ca3014e28d50cfcd3e2e8dd
- Delivery SHA: commit chứa báo cáo này — báo sau push

## B. AUD-23
- Root cause: tải vai trò/quyền trước khi verify mật khẩu; `Verify` không chạy PBKDF2 cho hash rỗng/sai định dạng.
- Fix: một truy vấn tìm user (đã lọc IsDeleted/IsActive/Status) → PBKDF2 (dummy khi không có user hoặc hash không dùng được) → tải quyền chỉ khi mật khẩu đúng; thông báo chung; xóa mật khẩu khỏi form. Cùng lỗi ở Customer Portal (AUD-38) đã sửa.
- Limitations: TIMING_NOT_VERIFIED; không constant-time toàn phần.

## C. AUD-24
- Findings: không giới hạn bộ nhớ; không cấu hình proxy; theo process.
- Changes: MaxTrackedKeys + fail-closed; allowlist proxy chính xác (mặc định tắt, bỏ trust loopback, cấu hình sai dừng khởi động); interface IAdminLoginThrottle.
- Infrastructure: store dùng chung/gateway/WAF — PENDING_INFRASTRUCTURE_APPROVAL; topology proxy chưa biết; PROXY_NOT_VERIFIED. Không tuyên bố đã giải quyết multi-instance.

## D. AUD-25/29
- Audit bền vững: mọi thao tác quản trị nhạy cảm có audit trong cùng transaction (bảng mục 2 audit report), gồm mới: tạo user, đổi mật khẩu Admin, khách hàng, membership (AUD-35/36/37).
- Fail-closed (RequireDurableAudit=true): bảng thiếu/không nhìn thấy, sai cột, INSERT lỗi → rollback; transaction doomed luôn rollback; không ghi audit giả; thông báo chung, không lộ SQL; cảnh báo khởi động khi tắt.
- Operator SQL: script 007 (visibility vs INSERT, quyền membership), migration 010 nếu thật sự thiếu bảng, GRANT INSERT theo template 009, runbook 8 bước. Cờ vẫn tắt — CONFIGURATION_PENDING.

## E. Security regression
- Auth/RBAC: SYS_ADMIN kiểm tra lại từ DB; không tự nâng quyền; SYS_ADMIN cuối được bảo vệ; Customer Portal không cấp quyền Admin.
- Tenant isolation, CSRF, tách cookie, no-store, thu hồi tài khoản/membership: test cũ PASS.
- SQL: tham số hóa; không lộ stack trace; log không chứa username/mật khẩu (test).

## F. New findings
| ID | Severity | Evidence | Action |
|---|---|---|---|
| AUD-35 | MEDIUM | Tạo user/đổi mật khẩu Admin không audit | FIXED (code) |
| AUD-36 | MEDIUM | Membership Customer Portal không audit | FIXED (code, Shared) |
| AUD-37 | LOW | Khách hàng không audit | FIXED (code) |
| AUD-38 | LOW | Customer Portal login timing | FIXED |
| (open) | LOW | Đổi mật khẩu Customer Portal chưa audit | PENDING_APPROVAL |

## G. Database
- Script 007 cập nhật (read-only, NOT_EXECUTED); migration 009/010 NOT_EXECUTED; quyền chưa cấp; DATABASE_NOT_VERIFIED.

## H. Changed files
- Source: Shared/AuditTrail.cs (mới), Shared/MembershipAdminStore.cs, Customer AccountController, Admin AccountController/CustomerPortalAccountsController/PortalControllers/Program/AdminLoginThrottle/CustomerAdminStore/PortalDb/UserAccountStore/UserRoleStore.
- Tests: SafeAdminHardeningTests (mới), SafePortalTests (+2), SafeAdminRoleTests (assertion).
- Configuration: không đổi appsettings; khóa mới tùy chọn `Security__AdminLogin__MaxTrackedKeys`, `ReverseProxy__KnownProxies__N`.
- Documents: database/scripts/007, docs/operations/durable-audit-enablement-runbook.md, docs/customer-portal-runbook.md, audit report.

## I. Build/tests
- Release build PASS 0/0. Safe tests 202 passed / 0 failed / 0 skipped (42 mới). Codec 4/4, validate PASS. Smoke Admin 15/15, Customer 10/10.
- Limitations: TIMING_NOT_VERIFIED, PROXY_NOT_VERIFIED, DATABASE_NOT_VERIFIED, BROWSER_NOT_VERIFIED.

## J. Publish
- TAPortal.Web: PUBLISHED (local) / NOT_DEPLOYED — D:\T.A\TAPortal\TAPortal\publish\deployed-TAPortal.Web; source b6eb780202049b9a8ca3014e28d50cfcd3e2e8dd; 2026-10-29T05:15:28Z; exit 0; 57 files; manifest .codec/reports/TAPORTAL-SYSTEM-AUDIT-CHG-20261029T050717Z-E1EAC3-TAPortal.Web.sha256; **artifact SHA256 64ff472d88eeb0f55e43564c4532802ae9196cce254fb2340143e422729fa10e**.
- TACustomerPortal.Web: PUBLISHED (local) / NOT_DEPLOYED — D:\T.A\TAPortal\TAPortal\publish\deployed-TACustomerPortal.Web; source b6eb780202049b9a8ca3014e28d50cfcd3e2e8dd; 2026-10-29T05:15:38Z; exit 0; 54 files; manifest .codec/reports/TAPORTAL-SYSTEM-AUDIT-CHG-20261029T050717Z-E1EAC3-TACustomerPortal.Web.sha256; **artifact SHA256 71c104f16f7914ef3e7cf3980b0f1e0372ef1d74baa8211e499b890c4767338a**.

## K. Git
- Source commit b6eb780202049b9a8ca3014e28d50cfcd3e2e8dd; delivery/push/remote HEAD trong thông báo bàn giao; PR #7; NOT_MERGED.

## L. Operator handoff
- Pending approvals: store throttle dùng chung/gateway (hạ tầng); audit đổi mật khẩu Customer Portal; các mục PENDING_APPROVAL từ CP6–8.
- Required configuration (ngoài Git): `Security__Audit__RequireDurableAudit=true` sau khi 007 PASS; `ReverseProxy__KnownProxies__N` chỉ khi có proxy.
- Final UAT prerequisites: runbook audit 8 bước; script 007/008; quyết định migration 009/010; proxy topology; UAT checklist CP5–9.
- Rollback: hai thư mục backup trong publish\rollback\TAPORTAL-SYSTEM-AUDIT-CHG-20261029T050717Z-E1EAC3\; `git revert b6eb780`; tắt cờ audit nếu đã bật.
- Checkpoint 10 readiness: READY (code đủ cho đợt deploy/UAT tổng thể; phụ thuộc operator/hạ tầng ghi ở trên). Trạng thái: READY_FOR_UAT, không production-ready.
