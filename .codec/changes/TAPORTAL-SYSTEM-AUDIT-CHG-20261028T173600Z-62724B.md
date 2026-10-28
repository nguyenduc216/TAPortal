# Change TAPORTAL-SYSTEM-AUDIT-CHG-20261028T173600Z-62724B

- Feature ID: `TAPORTAL-SYSTEM-AUDIT` (Checkpoint `CHECKPOINT-07-ACCOUNT-INTEGRITY`)
- Change ID: `TAPORTAL-SYSTEM-AUDIT-CHG-20261028T173600Z-62724B`
- Created UTC: 2026-10-28T17:36:00Z
- Tiêu đề: Checkpoint 7 account status, tax code integrity, user soft delete, audit preflight (AUD-15/16/17/25)
- Trạng thái: ADMIN_ARTIFACT_PUBLISHED_PARTIAL_PENDING_OPERATOR
- Agent: Claude Code (claude-opus-5-5)
- Quyết định operator: chưa deploy, chưa UAT tổng thể, chưa merge.

## Yêu cầu và phạm vi
AUD-15/16/17/25 trong phạm vi schema hiện có. Chi tiết: `docs/audits/taportal-checkpoint7-account-integrity-TAPORTAL-SYSTEM-AUDIT-CHG-20261028T173600Z-62724B.md`.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD src/TAPortal.Web/Services/UserAccountStore.cs (AUD-15/17/25/27/28), src/TAPortal.Web/Services/CustomerAdminStore.cs (AUD-16)
- MODIFY src/TAPortal.Web/Services/UserRoleStore.cs (AUD-25 RequireDurableAudit), src/TAPortal.Web/Services/PortalDb.cs (gỡ các hàm ghi cũ không có bảo vệ), src/TAPortal.Web/Controllers/PortalControllers.cs, src/TAPortal.Web/Models/PortalModels.cs, src/TAPortal.Web/Program.cs
- ADD src/TAPortal.Web/Views/Users/Delete.cshtml; MODIFY Views/Users/Edit.cshtml, Views/Users/Index.cshtml, Views/Customers/Edit.cshtml
- ADD tests/TACustomerPortal.Tests/SafeAdminAccountTests.cs (32 test)
- ADD database/scripts/007-verify-audit-logs.sql, 008-audit-customer-taxcode-duplicates.sql, 009-grant-admin-audit-insert-template.sql, 010-rollback-customer-taxcode-unique-index.sql
- ADD database/migrations/009-customer-taxcode-unique-index.sql, 010-create-audit-logs-if-missing.sql
- ADD docs/audits/taportal-checkpoint7-…md, .codec change/report/manifest; MODIFY feature README/history
- Không đụng: database/scripts/004-verify-customer-portal.sql (operator), database/scripts/scene-image.png, src/.claude/

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys — DATABASE_NOT_VERIFIED
- Thứ tự đề xuất: (1) scripts/007 [read-only] → (2) migrations/010 chỉ khi 007 báo thiếu bảng → (3) scripts/009 template GRANT (DBA) → (4) chạy lại 007 đến PASS → đặt `Security__Audit__RequireDurableAudit=true`; (5) scripts/008 [read-only] → (6) migrations/009 tùy chọn khi 008 PASS; rollback 009: scripts/010.
- Guard: `IF DB_NAME() <> N'TAPortalSys' THROW` ở mọi file; không có USE; migration dùng XACT_ABORT + transaction.
- Execution: MIGRATION_NOT_EXECUTED (009, 010); scripts NOT_EXECUTED.
- Artifact không phụ thuộc migration: chạy được trên schema hiện có.

## Test và kết quả
- Build Release PASS 0/0; safe tests 136 passed / 0 failed / 0 skipped (104 cũ + 32 mới); Codec 4/4, validate PASS; git diff --check PASS; smoke Admin 15/15.
- NOT_VERIFIED: khóa SQL, UPDATE xóa mềm thật, ghi AuditLogs, các script SQL.

## Publish
- TAPortal.Web: PUBLISHED, exit 0, 2026-10-28T17:44:27Z→17:44:29Z, 57 files, artifact SHA256 fc042e76e4c6a4e55c7a9a681106cc986e1114e6c9c97240f592750c9bbea727, DLL be56321509188e9c0b878c8355b49f5ba80d4ef0c1b4821912bcbf9b2693d128
- Bản trước 5adf697b… backup tại publish\rollback\TAPORTAL-SYSTEM-AUDIT-CHG-20261028T173600Z-62724B\deployed-TAPortal.Web; các backup trước được giữ nguyên
- TACustomerPortal.Web: NOT_REQUIRED (ea77b3be…ac34 không đổi)
- NOT_DEPLOYED; IIS NOT_CHECKED

## Git
- Branch feature/customer-portal-mvp01; starting 3fddd8c28a0f30cc29b39c75e7be323ce2d5b4db; source 6a2462b526de34e9f166541447c6e2f6d51bad59; delivery báo sau push
- PR https://github.com/nguyenduc216/TAPortal/pull/7; Merge PENDING_APPROVAL

## Rủi ro, quyết định và việc còn lại
- PENDING_APPROVAL: xóa mềm doanh nghiệp; khôi phục/purge/retention; ý nghĩa các giá trị Users.Status khác ACTIVE.
- PENDING_OPERATOR: script 007/008, migration 009/010, quyền INSERT AuditLogs, bật RequireDurableAudit.
- AUD-29 OPEN cho đến khi RequireDurableAudit=true.
- Rollback: khôi phục backup Admin; `git revert 6a2462b`; migration 009 → scripts/010.
