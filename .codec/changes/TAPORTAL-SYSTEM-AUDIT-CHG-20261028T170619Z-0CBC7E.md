# Change TAPORTAL-SYSTEM-AUDIT-CHG-20261028T170619Z-0CBC7E

- Feature ID: `TAPORTAL-SYSTEM-AUDIT` (Checkpoint `CHECKPOINT-05-ADMIN-SECURITY`)
- Change ID: `TAPORTAL-SYSTEM-AUDIT-CHG-20261028T170619Z-0CBC7E`
- Created UTC: 2026-10-28T17:06:19Z
- Tiêu đề: Checkpoint 5 Admin login rate limiting (AUD-13) and authenticated no-store (AUD-14)
- Trạng thái: ADMIN_ARTIFACT_PUBLISHED_UAT_PENDING
- Agent: Claude Code (claude-opus-5-5)

## Yêu cầu và phạm vi
Sửa AUD-13 và AUD-14 cho TAPortal Admin, kèm regression test tất định, build, publish cục bộ các project bị ảnh hưởng, báo cáo. Ngoài phạm vi: AUD-12, schema/SQL, IIS, merge. Chi tiết: `docs/audits/taportal-checkpoint5-admin-security-TAPORTAL-SYSTEM-AUDIT-CHG-20261028T170619Z-0CBC7E.md`.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD src/TAPortal.Web/Security/AdminLoginThrottle.cs — AUD-13
- MODIFY src/TAPortal.Web/Controllers/AccountController.cs — AUD-13, AUD-20, AUD-14 (Clear-Site-Data)
- MODIFY src/TAPortal.Web/Program.cs — AUD-13 (DI/cấu hình), AUD-14 (middleware), AUD-22
- MODIFY src/TAPortal.Web/Views/Account/Login.cshtml — AUD-21
- ADD tests/TACustomerPortal.Tests/SafeAdminSecurityTests.cs — 19 test
- MODIFY AGENTS.md, .agents/skills/codec/SKILL.md — filter safe test chung `FullyQualifiedName~TACustomerPortal.Tests.Safe`
- MODIFY docs/customer-portal-runbook.md — lệnh test, cấu hình và giới hạn
- ADD docs/audits/taportal-checkpoint5-admin-security-…md, .codec change/report/manifest; MODIFY feature README/history
- Không đụng: database/scripts/004-verify-customer-portal.sql, database/scripts/scene-image.png, src/.claude/

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys — không truy cập (DATABASE_NOT_VERIFIED)
- Script: không có; không đổi schema
- Execution: NONE_REQUIRED / NOT_EXECUTED

## Test và kết quả
- dotnet build TAPortal.sln -c Release --no-restore: PASS, 0 warnings, 0 errors (restore up-to-date)
- dotnet test … --filter "FullyQualifiedName~TACustomerPortal.Tests.Safe": 84 passed / 0 failed / 0 skipped (34 Customer + 31 Admin CRUD + 19 mới); TRX TestResults/checkpoint5.trx (ignored)
- Codec: 4 unit tests exit 0; validate exit 0; git diff --cached --check PASS
- Smoke artifact Admin (Production, không SQL): 11/11 PASS

## Publish
- TAPortal.Web → D:\T.A\TAPortal\TAPortal\publish\deployed-TAPortal.Web: PUBLISHED, exit 0, 2026-10-28T17:12:30Z→17:12:32Z, 57 files, artifact SHA256 42d6a7334a744094e4a8135463801a477b93fe86dd5e811c93cedf311e119cb4, DLL c64ce149392fa0a9d6e8786cc9efdcb5682a7331621ce5cca849aef34be973f4
- Bản trước 4b308e3b… đã được xác minh rồi backup tại publish\rollback\TAPORTAL-SYSTEM-AUDIT-CHG-20261028T170619Z-0CBC7E\deployed-TAPortal.Web (hash khớp)
- TACustomerPortal.Web: NOT_REQUIRED (không thay đổi Customer/Shared); artifact giữ nguyên ea77b3be…ac34 (đã kiểm tra lại)
- NOT_DEPLOYED; IIS NOT_CHECKED

## Git
- Branch: feature/customer-portal-mvp01
- Starting SHA: 1e1dda00255dd9fefa4db18ada8076f406e2bb68
- Source commit SHA: 183414102691ffb373d436951b27cf191d902fc2
- Delivery commit SHA: báo sau khi push (không tự tham chiếu)
- PR: https://github.com/nguyenduc216/TAPortal/pull/7
- Merge: PENDING_APPROVAL; Merge SHA: none

## Rủi ro, quyết định và việc còn lại
- AUD-23 (chênh lệch thời gian nhỏ còn lại) OPEN; AUD-24 (bộ đếm theo từng process/proxy) OPEN — cần gateway/WAF.
- Rollback: khôi phục thư mục backup Admin; `git revert 1834141`.
