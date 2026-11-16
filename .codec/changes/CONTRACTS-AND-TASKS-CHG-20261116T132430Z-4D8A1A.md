# Change CONTRACTS-AND-TASKS-CHG-20261116T132430Z-4D8A1A

- Feature ID: `CONTRACTS-AND-TASKS`
- Change ID: `CONTRACTS-AND-TASKS-CHG-20261116T132430Z-4D8A1A`
- Created UTC: 2026-11-16T13:24:30.466446+00:00
- Tiêu đề: Push notifications for every task change and a daily due reminder
- Trạng thái: PLANNED

## Yêu cầu và phạm vi
Operator 2026-11-16: "Tất cả các thông báo" (after web push in CONTRACTS-AND-TASKS-CHG-20261116T123108Z-D0F409).
- `WorkPushWorker` (hosted service in TAPortal.Web, every 15 s; operator 2026-11-16: changed to once a minute): reads new `dbo.WorkTaskActivities` rows (created, assign, status, comment, checklist except reorder, edit/documents) and pushes one notification per person and task (owner, active co-workers, creator; never the actor; only users with a device). Watermark in `dbo.WebPushState`; 10 s settle window; rows older than 1 h skipped.
- Daily from 08:00 Vietnam time: one reminder per person (overdue / due today / due tomorrow, open tasks), claimed once per day in `dbo.WebPushState`.
- IIS note (read-only check): pool TA.Portal startMode OnDemand, idleTimeout 20 min, preload off. Task changes come from requests, so the worker is awake then; the 08:00 reminder waits for the first visit if the pool slept. AlwaysRunning + preload is an operator change (not done by the agent).

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `database/migrations/060-work-push-notifications.sql`, `database/scripts/107-verify-work-push-notifications.sql`, `database/scripts/108-rollback-work-push-notifications.sql`, `src/TAPortal.Web/Services/WorkPushWorker.cs`
- MODIFY `src/TAPortal.Web/Program.cs`, `src/TAPortal.Web/Views/Account/Profile.cshtml`, `tests/TACustomerPortal.Tests/SafeWebPushTests.cs`
- SQL of the worker checked on a throwaway LocalDB database with sample rows (recipients, actor excluded, due counts).

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path:
- Thứ tự chạy:
- Preflight / Verify / Rollback:
- Execution: NOT_EXECUTED

## Test và kết quả

## Publish
- Project:
- Destination (chỉ từ config):
- Trạng thái: NOT_PUBLISHED
- Artifact SHA256:
- Smoke test:

## Git
- Branch:
- Commit SHA:
- PR:
- Merge: PENDING_APPROVAL
- Merge SHA:

## Rủi ro, quyết định và việc còn lại

## Migration execution
- 060-work-push-notifications.sql: PRECHECK NOT_APPLIED; DRYRUN PASS/NOT_APPLIED (trancount 0); gates PASS (792); REAL RUN VERIFY_STATUS=PASS => EXECUTED 2026-11-16T13:28:13Z

## Deploy
- portal (TAPortal.Web): commit d0f03f3, artifact a641332868455b5dc43d3e626d39fa6747dd650107856776d24872c8db66ea9b, package 3664409307DFBC1A862CD6F151E29C96583D2E734AE17E26D2BD7980E827C14C, DEPLOYED 2026-11-16T13:33:51Z, health 200.
- Smoke (read-only query): dbo.WebPushState created by the worker at 13:34:10Z and advancing; reminder day 2026-11-16 claimed (0 devices registered yet). Delivery to a real phone NOT_VERIFIED (operator to enable on a device).
- portal: commit 10674c3 (worker once a minute), artifact 82532188b4517fcfc80d4d6e1091bcc7d5a269f62fce4db20282d260e8a723a9, package B0988677F0FF4A7219689F9731E1BEFEA772C7584C9A9C86D0FB1A0C5F458DDE, DEPLOYED 2026-11-16T13:46:17Z, health 200.
