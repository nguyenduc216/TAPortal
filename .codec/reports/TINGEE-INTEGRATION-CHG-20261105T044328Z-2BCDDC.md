# CODEC DELIVERY REPORT — TINGEE-INTEGRATION-CHG-20261105T044328Z-2BCDDC

Feature ID: `TINGEE-INTEGRATION`
Change ID: `TINGEE-INTEGRATION-CHG-20261105T044328Z-2BCDDC`

1. Mục tiêu và kết quả thực tế:
2. Danh sách file cập nhật (relative + nếu cần absolute):
3. Database `TAPortalSys`: migration path + thứ tự + trạng thái NOT_EXECUTED/OPERATOR_CONFIRMED:
4. Build/test: lệnh, pass/fail/not run:
5. Publish: project, destination đã duyệt, artifact SHA256, status:
6. Git: branch, commit SHA, PR, merge SHA hoặc PENDING_APPROVAL:
7. Rủi ro/rollback/việc operator cần làm:
8. Bằng chứng kiểm tra (URL/log, không có secrets):


## Deploy (2026-11-05)
- webhooks (TAPortal.Webhooks → TA.Webhooks): DEPLOYED 2026-11-05 12:08 (UTC+7), commit 556e987, artifact SHA256 04b44a470f164cbd94b2ec395035f3e8a0737f43082510e093c7bea119fa13a8, package 8A3F1ED1D6AC70F7A7F71833B5BE0337A39279CB3C61AD48BE83A1840484545F; backup C:\TADeployackups\webhooks-20261105-120806; health HTTP 200 (version 1.0.0+556e987, database ok).
- invoice (TACustomerPortal.Web → TA.Invoice): DEPLOYED 2026-11-05 12:08 (UTC+7), commit 556e987, artifact SHA256 8f9ef2adca356c136539a7212b25f194d23458cc3392657accbebf9dd5dd99df, package 95BEF4D887B21C15F16B70C093192C67838D8E7F25BC1BF58D4AC708DE26F1DB; backup C:\TADeployackups\invoice-20261105-120817; health HTTP 200.
- portal (TAPortal.Web → TA.Portal): DEPLOYED 2026-11-05 12:08 (UTC+7), commit 556e987, artifact SHA256 f6adc9321d53aef6c41d2e2d1c7b763d1cb100c397a8c3ae6253de4c5acbcc34, package F6477D74D77CD7297B99A7FD3E42EA7DDF1AE6E3904AECA40DC3ADB38B450E56; backup C:\TADeployackups\portal-20261105-120828; health HTTP 200.
- Server backups after deploy: exactly one per target (KeepBackups 1, operator rule 2026-11-05). Deploy-TARemote -DryRun run first (script changed): audit passed.
- Not verified: a real bank transfer end to end; SQL counts on production (read blocked by the agent's safety classifier).

Migrations: 019 and 020 EXECUTED (see change record and `.codec/reports/TINGEE-INTEGRATION-CHG-20261105T042908Z-94446D-migrations-20261105T050148Z.log`).
