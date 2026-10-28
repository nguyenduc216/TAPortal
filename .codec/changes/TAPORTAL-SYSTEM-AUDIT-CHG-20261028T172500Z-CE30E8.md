# Change TAPORTAL-SYSTEM-AUDIT-CHG-20261028T172500Z-CE30E8

- Feature ID: `TAPORTAL-SYSTEM-AUDIT` (Checkpoint `CHECKPOINT-06-ROLE-MANAGEMENT`)
- Change ID: `TAPORTAL-SYSTEM-AUDIT-CHG-20261028T172500Z-CE30E8`
- Created UTC: 2026-10-28T17:25:00Z
- Tiêu đề: Checkpoint 6 user role management (AUD-12)
- Trạng thái: ADMIN_ARTIFACT_PUBLISHED_UAT_PENDING
- Agent: Claude Code (claude-opus-5-5)
- Quyết định operator: chưa deploy server, chưa UAT tổng thể; UAT/deploy một lần sau checkpoint cuối; không merge main.

## Yêu cầu và phạm vi
AUD-12: SYS_ADMIN quản lý vai trò người dùng trên mô hình Roles/UserRoles sẵn có, bảo vệ SYS_ADMIN cuối cùng, chống ghi đè đồng thời, audit khi có. Ngoài phạm vi: ma trận quyền, tạo role, UserPermissions trực tiếp (PENDING_APPROVAL), AUD-15..18, AUD-23/24. Chi tiết: `docs/audits/taportal-checkpoint6-role-management-TAPORTAL-SYSTEM-AUDIT-CHG-20261028T172500Z-CE30E8.md`.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD src/TAPortal.Web/Services/UserRoleStore.cs — policy, interface, SQL store
- MODIFY src/TAPortal.Web/Controllers/PortalControllers.cs — Roles GET/POST trên UsersController
- MODIFY src/TAPortal.Web/Services/PortalDb.cs — GetUserRoleMapAsync (chỉ đọc)
- MODIFY src/TAPortal.Web/Program.cs — đăng ký IUserRoleStore
- ADD src/TAPortal.Web/Views/Users/Roles.cshtml; MODIFY Views/Users/Index.cshtml, Views/Users/Edit.cshtml
- ADD tests/TACustomerPortal.Tests/SafeAdminRoleTests.cs — 20 test
- ADD docs/audits/taportal-checkpoint6-…md, .codec change/report/manifest; MODIFY feature README/history
- Không đụng: database/scripts/004-verify-customer-portal.sql, database/scripts/scene-image.png, src/.claude/

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys — DATABASE_NOT_VERIFIED (MCP chỉ đọc lỗi kết nối)
- Script: không có; không đổi schema
- Execution: NONE_REQUIRED / NOT_EXECUTED

## Test và kết quả
- Build Release: PASS 0 warnings / 0 errors (restore up-to-date)
- Safe tests `FullyQualifiedName~TACustomerPortal.Tests.Safe`: 104 passed / 0 failed / 0 skipped (84 cũ + 20 mới); TRX TestResults/checkpoint6.trx (ignored)
- Codec: 4 unit tests exit 0; validate exit 0; git diff --cached --check PASS
- Smoke artifact Admin (Production, không SQL): 12/12 PASS (gồm hồi quy AUD-13/14)
- NOT_VERIFIED: khóa/transaction SQL thật, ghi AuditLogs thật, claims cập nhật trên trình duyệt

## Publish
- TAPortal.Web → D:\T.A\TAPortal\TAPortal\publish\deployed-TAPortal.Web: PUBLISHED, exit 0, 2026-10-28T17:29:11Z→17:29:13Z, 57 files, artifact SHA256 5adf697b9fab26d521761cdb81d667121eb49147939bb1a3bfe9326c3c4afd36, DLL 34e0e997fcf1a8523f60147225b0054f091a0ced4255fbd0efbddb099cd3a72d
- Bản trước 42d6a733… được xác minh rồi backup tại publish\rollback\TAPORTAL-SYSTEM-AUDIT-CHG-20261028T172500Z-CE30E8\deployed-TAPortal.Web
- TACustomerPortal.Web: NOT_REQUIRED (không đổi Customer/Shared); ea77b3be…ac34 giữ nguyên, đã kiểm tra lại
- NOT_DEPLOYED; IIS NOT_CHECKED

## Git
- Branch: feature/customer-portal-mvp01
- Starting SHA: 900db91ecb1671c072a958cd240ee12064cc76cb
- Source commit SHA: 36db1038ebe071ebab4f588538e118d79fb416e9
- Delivery commit SHA: báo sau khi push
- PR: https://github.com/nguyenduc216/TAPortal/pull/7
- Merge: PENDING_APPROVAL; Merge SHA: none

## Rủi ro, quyết định và việc còn lại
- AUD-25 PENDING_OPERATOR: xác nhận dbo.AuditLogs + quyền INSERT; thiếu quyền INSERT làm thao tác đổi vai trò thất bại (fail-closed).
- AUD-12a PENDING_APPROVAL; AUD-26 OPEN.
- Rollback: khôi phục backup Admin; `git revert 36db103`.
