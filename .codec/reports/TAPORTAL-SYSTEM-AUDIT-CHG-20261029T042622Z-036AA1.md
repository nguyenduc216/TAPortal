# TAPORTAL CHECKPOINT 8 — DASHBOARD & UX DELIVERY REPORT

Feature ID: `TAPORTAL-SYSTEM-AUDIT` · Change ID: `TAPORTAL-SYSTEM-AUDIT-CHG-20261029T042622Z-036AA1` · Agent: Claude Code (claude-opus-5-5)

## A. Identification
- FEATURE_ID TAPORTAL-SYSTEM-AUDIT; CHANGE_ID TAPORTAL-SYSTEM-AUDIT-CHG-20261029T042622Z-036AA1; branch feature/customer-portal-mvp01
- Starting SHA 20558b4b56be9dc1f1f527366e9e0952f451cfcd (bằng origin, không có commit agent khác)
- Source SHA 2013ea2a10be1f4f618a65c9733adabfda1cc873
- Delivery SHA: commit chứa báo cáo này — báo sau push

## B. AUD-18
- Root cause (báo cáo gốc): badge "Cơ sở dữ liệu: Kết nối" cố định; xác minh lại thêm badge "Ứng dụng"/"RBAC" cố định, placeholder "Hoạt động gần đây", KPI không định nghĩa, không có error state.
- Implementation: `SqlDashboardStore` + view mới; trạng thái DB = đọc dữ liệu thành công hay không; trạng thái bảng audit; cấu hình RequireDurableAudit/throttle; thay đổi gần đây từ AuditLogs.
- Evidence: 10 test dashboard + smoke; SQL thật NOT_VERIFIED. Status: FIXED.

## C. Dashboard
- KPI và định nghĩa: xem audit report mục 3 (users active/disabled/locked/current, xóa mềm riêng; customers active/inactive; membership có hiệu lực + tài khoản DISTINCT; vai trò hoạt động).
- Data sources: dbo.Users, dbo.Customers, dbo.Roles, dbo.CustomerPortalMemberships (JOIN Users/Customers, một dòng/membership), dbo.AuditLogs (TOP 5, không Old/NewValues).
- Empty/error: DB lỗi → cảnh báo + trạng thái "Không đọc được", không số; bảng membership thiếu → "Chưa có dữ liệu"; AuditLogs thiếu / không SELECT / rỗng → thông báo riêng.

## D. UX
- Users: nhãn trạng thái thực, ẩn link xóa với chính mình; Roles: quyền trực tiếp chỉ đọc, ghi chú SYS_ADMIN cuối; Customers: giữ thông báo MST; Memberships: "Đang có hiệu lực / Không hiệu lực + lý do / Đã thu hồi"; Navigation: toggler mobile, landmark `<nav>`, `aria-current`, AccessDenied có đích hợp lệ.

## E. Security
- Dashboard SystemAdminOnly (OWNER + permission → 403); không thêm route ghi; CSRF giữ nguyên; output Razor encode (test chèn `<script>` vào tên actor); không lộ exception (test); no-store giữ nguyên (test + smoke); không đọc dữ liệu tenant ngoài thống kê cho SYS_ADMIN; quyền trực tiếp chỉ hiển thị, không form.

## F. Regression
- 136 test trước PASS: AUD-12, AUD-13, AUD-14, AUD-15, AUD-16, AUD-17, AUD-20/21/22, AUD-27/28, tenant isolation, OWNER/ADMIN/VIEWER. Smoke 15/15 (throttle 429, no-store, static cache). AuditLogs policy không đổi; RequireDurableAudit vẫn tắt.

## G. Database
- Không có migration; không chạy SQL; DATABASE_NOT_VERIFIED. Dashboard chịu được việc thiếu bảng membership/AuditLogs. Migration 009/010 (CP7) vẫn NOT_EXECUTED.

## H. Changed files
| File | Tóm tắt |
|---|---|
| Services/DashboardStore.cs (mới) | KPI, phân loại trạng thái, store SQL |
| Services/MembershipDisplay.cs (mới) | Hiệu lực membership |
| Services/UserRoleStore.cs | Đọc UserPermissions, IsLastSystemAdmin |
| Services/PortalDb.cs | Gỡ GetDashboardAsync |
| Controllers/PortalControllers.cs | HomeController dùng store, error state |
| Models/PortalModels.cs | DashboardPageVm |
| Program.cs | DI IDashboardStore |
| Views/Home/Index.cshtml | Dashboard mới |
| Views/Shared/_Layout.cshtml | Toggler, nav, aria |
| Views/Account/AccessDenied.cshtml | Link theo quyền |
| Views/Users/Roles.cshtml, Users/Edit.cshtml | Quyền trực tiếp, SYS_ADMIN cuối, ẩn link xóa |
| Views/CustomerPortalAccounts/Index.cshtml | Trạng thái hiệu lực |
| wwwroot/js/ta-portal.js | aria-current |
| tests/…/SafeAdminDashboardUxTests.cs (mới) | 24 test |

## I. Build/tests
- Release build PASS 0/0. Safe tests 160 passed / 0 failed / 0 skipped (24 mới). Codec 4/4, validate PASS. Smoke 15/15.
- Limitations: SQL tổng hợp NOT_VERIFIED; BROWSER_NOT_VERIFIED (responsive/keyboard/contrast chỉ kiểm tra qua markup).

## J. Publish
- Admin TAPortal.Web: PUBLISHED (local) / NOT_DEPLOYED — D:\T.A\TAPortal\TAPortal\publish\deployed-TAPortal.Web; source 2013ea2a10be1f4f618a65c9733adabfda1cc873; 2026-10-29T04:32:08Z; exit 0; 57 files; manifest .codec/reports/TAPORTAL-SYSTEM-AUDIT-CHG-20261029T042622Z-036AA1-TAPortal.Web.sha256; **artifact SHA256 a6aca5d0ac8fc3a76b353745503763b428e94d84286e8fe0d18ee8a46b7cc5ff**.
- Customer: NOT_REQUIRED — ea77b3be501ea28fe80ce54665a805b6798b502137347ce11f7d356547f8ac34 (source 336ab7c) không đổi.

## K. Git
- Source commit 2013ea2a10be1f4f618a65c9733adabfda1cc873; delivery/push/remote HEAD trong thông báo bàn giao; PR #7; NOT_MERGED.

## L. Handoff
- Open: AUD-23, AUD-24, AUD-25, AUD-29 (Checkpoint 9/operator); PENDING_APPROVAL: UserPermissions trực tiếp, xóa mềm doanh nghiệp, Users.Status.
- UAT: audit report mục 6 (gộp đợt UAT tổng thể).
- Rollback: backup Admin trong publish\rollback\…; `git revert 2013ea2`.
- Checkpoint 9 readiness: READY — backlog bảo mật và script SQL operator đã sẵn sàng.
- Trạng thái: READY_FOR_UAT; không production-ready.
