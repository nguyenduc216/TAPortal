# Change TAPORTAL-SYSTEM-AUDIT-CHG-20261029T050717Z-E1EAC3

- Feature ID: `TAPORTAL-SYSTEM-AUDIT` (Checkpoint `CHECKPOINT-09-SECURITY-HARDENING`)
- Change ID: `TAPORTAL-SYSTEM-AUDIT-CHG-20261029T050717Z-E1EAC3`
- Created UTC: 2026-10-29T05:07:17Z
- Tiêu đề: Checkpoint 9 security hardening: login timing (AUD-23), throttle/proxy (AUD-24), durable audit (AUD-25/29)
- Trạng thái: BOTH_ARTIFACTS_PUBLISHED_PARTIAL_PENDING_OPERATOR
- Agent: Claude Code (claude-opus-5-5)

## Yêu cầu và phạm vi
AUD-23/24/25/29 ở tầng ứng dụng, rà soát audit các thao tác nhạy cảm, hồi quy bảo mật CP5–8. Không SQL, không proxy/IIS, không tự bật RequireDurableAudit. Chi tiết: `docs/audits/taportal-checkpoint9-security-hardening-TAPORTAL-SYSTEM-AUDIT-CHG-20261029T050717Z-E1EAC3.md`.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD src/TAPortal.Shared/AuditTrail.cs (AUD-29); MODIFY src/TAPortal.Shared/MembershipAdminStore.cs (AUD-36)
- MODIFY src/TACustomerPortal.Web/Controllers/AccountController.cs (AUD-38)
- MODIFY src/TAPortal.Web/Controllers/AccountController.cs (AUD-23/35), Controllers/CustomerPortalAccountsController.cs (AUD-36), Controllers/PortalControllers.cs (AUD-35/37), Program.cs (AUD-24/29), Security/AdminLoginThrottle.cs (AUD-24), Services/CustomerAdminStore.cs (AUD-37), Services/PortalDb.cs (AUD-23/35), Services/UserAccountStore.cs, Services/UserRoleStore.cs (AUD-29)
- ADD tests/TACustomerPortal.Tests/SafeAdminHardeningTests.cs (40); MODIFY SafePortalTests.cs (+2), SafeAdminRoleTests.cs (assertion câu audit)
- MODIFY database/scripts/007-verify-audit-logs.sql (AUD-25); ADD docs/operations/durable-audit-enablement-runbook.md; MODIFY docs/customer-portal-runbook.md
- ADD docs/audits/taportal-checkpoint9-…md, .codec change/report/2 manifest; MODIFY feature README/history
- Không đụng: database/scripts/004-verify-customer-portal.sql, database/scripts/scene-image.png, src/.claude/

## Migration SQL (KHÔNG tự chạy)
- Không có migration mới. Script 007 được cập nhật (chỉ đọc, chưa chạy). Migration 009/010 (CP7) vẫn NOT_EXECUTED.

## Test và kết quả
- Build Release PASS 0/0; safe tests 202 passed / 0 failed / 0 skipped (42 mới); Codec 4/4, validate PASS; git diff --check PASS; smoke Admin 15/15, Customer 10/10.
- NOT_VERIFIED: timing thực tế, proxy thật, transaction/audit trên TAPortalSys.

## Publish
- TAPortal.Web: PUBLISHED, exit 0, 2026-10-29T05:15:25Z→05:15:28Z, 57 files, artifact SHA256 64ff472d88eeb0f55e43564c4532802ae9196cce254fb2340143e422729fa10e
- TACustomerPortal.Web: PUBLISHED (Shared + login thay đổi), exit 0, 2026-10-29T05:15:37Z→05:15:38Z, 54 files, artifact SHA256 71c104f16f7914ef3e7cf3980b0f1e0372ef1d74baa8211e499b890c4767338a
- TAPortal.Shared.dll giống nhau ở hai artifact: 0e22c2a8887f5d54a8338bbb253ceb23647335d672539efb1a0821d4a769b574
- Backup: publish\rollback\TAPORTAL-SYSTEM-AUDIT-CHG-20261029T050717Z-E1EAC3\ (a6aca5d0… và ea77b3be…); các backup cũ giữ nguyên
- NOT_DEPLOYED; IIS NOT_CHECKED; CONFIGURATION_PENDING (RequireDurableAudit, KnownProxies nếu có proxy)

## Git
- Branch feature/customer-portal-mvp01; starting 8fea21674aa8dd24a065bbd6204117ea6669d2f2; source b6eb780202049b9a8ca3014e28d50cfcd3e2e8dd; delivery báo sau push
- PR https://github.com/nguyenduc216/TAPortal/pull/7; Merge PENDING_APPROVAL

## Rủi ro, quyết định và việc còn lại
- PENDING_OPERATOR: runbook audit; PENDING_INFRASTRUCTURE_APPROVAL: proxy/gateway/shared throttle store.
- PENDING_APPROVAL: audit đổi mật khẩu Customer Portal.
- Rollback: khôi phục hai thư mục backup; `git revert b6eb780`.
