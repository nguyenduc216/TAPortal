# Change TAPORTAL-SYSTEM-AUDIT-CHG-20261029T052755Z-8ECA9D

- Feature ID: `TAPORTAL-SYSTEM-AUDIT` (Checkpoint `CHECKPOINT-10-RELEASE-CANDIDATE`)
- Change ID: `TAPORTAL-SYSTEM-AUDIT-CHG-20261029T052755Z-8ECA9D`
- Created UTC: 2026-10-29T05:27:55Z
- Tiêu đề: Checkpoint 10 final regression, release candidate and deployment readiness
- Trạng thái: RELEASE_CANDIDATE_READY_CONDITIONAL_GO
- Agent: Claude Code (claude-opus-5-5)

## Yêu cầu và phạm vi
Tổng kiểm tra CP1–9, ma trận findings, sửa lỗi an toàn còn lại, cặp release candidate từ một SHA, runbook triển khai/UAT/rollback, quyết định release. Không deploy, không SQL, không merge.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD src/TAPortal.Shared/SecurityHeaders.cs (AUD-39); MODIFY src/TAPortal.Web/Program.cs, src/TACustomerPortal.Web/Program.cs
- MODIFY database/scripts/003-customer-portal-schema-audit.sql (AUD-40, guard; không chạy)
- ADD tests/TACustomerPortal.Tests/SafeReleaseRegressionTests.cs (13)
- ADD docs/operations/final-deployment-uat-runbook.md; docs/audits/taportal-system-audit-final-TAPORTAL-SYSTEM-AUDIT-CHG-20261029T052755Z-8ECA9D.md
- ADD .codec change/report/2 manifest; MODIFY feature README/history
- Không đụng: database/scripts/004-verify-customer-portal.sql (operator đang sửa, chưa commit), database/scripts/scene-image.png, src/.claude/, scripts/001–002 (AUD-41, phạm vi DBA)

## Migration SQL (KHÔNG tự chạy)
- Không có migration mới. Trạng thái: 008 OPERATOR_REPORTED (CP3) / NOT_VERIFIED; 009, 010 NOT_EXECUTED; scripts 003/004/007/008 read-only chưa chạy; template 006/009 chưa áp dụng.
- Thứ tự cho operator: runbook Phase A (A2 → A3 → A4 → A5 → A6 → A7).

## Test và kết quả
- Build Release PASS 0/0; safe tests 215 passed / 0 failed / 0 skipped (13 mới); Codec 4/4, validate PASS; git diff --check PASS; smoke Admin 16/16, Customer 10/10 (kèm security headers).
- NOT_VERIFIED: DATABASE, BROWSER, PROXY, IIS, TIMING.

## Publish (release candidate)
- TAPortal.Web: exit 0, 2026-10-29T05:30:26Z→05:30:28Z, 57 files, SHA256 6cfdc285c736386f8e1b6a94303286a07cc0bbf0c912d685aa4db122e5edfe69
- TACustomerPortal.Web: exit 0, 2026-10-29T05:30:37Z→05:30:38Z, 54 files, SHA256 cb152b0567270cb4fe64d8b2aa01dd20007fac1c30ec4b7c7b4403c35cfe3efc
- Cùng source 95e64cedb14479850bc3f32d28ec0aa6c36f7d02; Shared DLL dca4b1ad… ở cả hai; appsettings ConnectionString rỗng, không có secret (appsettings.Development.json chỉ logging)
- Backup: publish\rollback\TAPORTAL-SYSTEM-AUDIT-CHG-20261029T052755Z-8ECA9D\ (64ff472d…, 71c104f1…); các backup cũ giữ nguyên
- NOT_DEPLOYED; IIS NOT_CHECKED

## Git
- Branch feature/customer-portal-mvp01; starting 1ebbb016bc5891b61b5bdce4024ebfa470e888d1; source 95e64cedb14479850bc3f32d28ec0aa6c36f7d02; delivery báo sau push
- PR https://github.com/nguyenduc216/TAPortal/pull/7; Merge PENDING_APPROVAL

## Rủi ro, quyết định và việc còn lại
- Quyết định: code RC READY; deployment CONDITIONAL GO; production NOT READY.
- Operator: Phase A–D runbook; AUD-16/25/29/41; UAT.
- Approval: AUD-24 (hạ tầng), AUD-42, ma trận quyền, xóa mềm doanh nghiệp, Users.Status.
- Rollback: backup trong publish\rollback\…\; `git revert 95e64ce`.
