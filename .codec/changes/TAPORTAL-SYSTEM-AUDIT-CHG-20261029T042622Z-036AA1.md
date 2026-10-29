# Change TAPORTAL-SYSTEM-AUDIT-CHG-20261029T042622Z-036AA1

- Feature ID: `TAPORTAL-SYSTEM-AUDIT` (Checkpoint `CHECKPOINT-08-DASHBOARD-UX`)
- Change ID: `TAPORTAL-SYSTEM-AUDIT-CHG-20261029T042622Z-036AA1`
- Created UTC: 2026-10-29T04:26:22Z
- Tiêu đề: Checkpoint 8 dashboard data (AUD-18), admin UX consistency, direct permissions view (AUD-26)
- Trạng thái: ADMIN_ARTIFACT_PUBLISHED_UAT_PENDING
- Agent: Claude Code (claude-opus-5-5)
- Quyết định operator: chưa deploy, chưa UAT tổng thể, chưa merge PR #7.

## Yêu cầu và phạm vi
AUD-18 theo báo cáo gốc; rà soát UX Admin; AUD-26 chỉ đọc. Không BI/biểu đồ/module mới; không đổi quyền/quy tắc; không xóa mềm doanh nghiệp; không bật RequireDurableAudit. Chi tiết: `docs/audits/taportal-checkpoint8-dashboard-ux-TAPORTAL-SYSTEM-AUDIT-CHG-20261029T042622Z-036AA1.md`.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD src/TAPortal.Web/Services/DashboardStore.cs (AUD-18), src/TAPortal.Web/Services/MembershipDisplay.cs (AUD-32)
- MODIFY src/TAPortal.Web/Services/UserRoleStore.cs (AUD-26 đọc UserPermissions), Services/PortalDb.cs (gỡ GetDashboardAsync), Controllers/PortalControllers.cs (HomeController), Models/PortalModels.cs (DashboardPageVm thay DashboardVm), Program.cs (DI)
- MODIFY Views/Home/Index.cshtml (AUD-18), Views/Shared/_Layout.cshtml (AUD-30/34), Views/Account/AccessDenied.cshtml (AUD-31), Views/Users/Roles.cshtml (AUD-26/33), Views/Users/Edit.cshtml (AUD-33), Views/CustomerPortalAccounts/Index.cshtml (AUD-32), wwwroot/js/ta-portal.js (AUD-34)
- ADD tests/TACustomerPortal.Tests/SafeAdminDashboardUxTests.cs (24 test)
- ADD docs/audits/taportal-checkpoint8-…md, .codec change/report/manifest; MODIFY feature README/history
- Không đụng: database/scripts/004-verify-customer-portal.sql, database/scripts/scene-image.png, src/.claude/

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys — không truy cập. Không có migration mới; dashboard chỉ đọc, chịu được trường hợp thiếu bảng membership/AuditLogs.
- Execution: NONE_REQUIRED. Migration 009/010 của Checkpoint 7 vẫn NOT_EXECUTED.

## Test và kết quả
- Build Release PASS 0/0; safe tests 160 passed / 0 failed / 0 skipped (136 + 24 mới); Codec 4/4, validate PASS; git diff --check PASS; smoke Admin 15/15.
- NOT_VERIFIED: tổng hợp SQL trên TAPortalSys; BROWSER_NOT_VERIFIED (responsive, bàn phím, focus, tương phản).

## Publish
- TAPortal.Web: PUBLISHED, exit 0, 2026-10-29T04:32:06Z→04:32:08Z, 57 files, artifact SHA256 a6aca5d0ac8fc3a76b353745503763b428e94d84286e8fe0d18ee8a46b7cc5ff, DLL b12bc601d86af071208fe6b33cfc836b9e7b5f799452d3a6a9daca859f44172f
- Bản trước fc042e76… backup tại publish\rollback\TAPORTAL-SYSTEM-AUDIT-CHG-20261029T042622Z-036AA1\deployed-TAPortal.Web; 4 bộ backup trước được giữ
- TACustomerPortal.Web: NOT_REQUIRED (ea77b3be…ac34)
- NOT_DEPLOYED; IIS NOT_CHECKED

## Git
- Branch feature/customer-portal-mvp01; starting 20558b4b56be9dc1f1f527366e9e0952f451cfcd; source 2013ea2a10be1f4f618a65c9733adabfda1cc873; delivery báo sau push
- PR https://github.com/nguyenduc216/TAPortal/pull/7; Merge PENDING_APPROVAL

## Rủi ro, quyết định và việc còn lại
- Backlog Checkpoint 9/operator: AUD-23, AUD-24, AUD-25, AUD-29 (RequireDurableAudit vẫn tắt).
- PENDING_APPROVAL: sửa UserPermissions trực tiếp, xóa mềm doanh nghiệp, Users.Status.
- Rollback: khôi phục backup Admin; `git revert 2013ea2`.
