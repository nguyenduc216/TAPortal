# Codec — TAPortal Codex Workflow Kit

Bộ quy tắc và công cụ cho Codex trong repository TAPortal. Không phải plugin ChatGPT đã cài đặt.

## Cài vào repository
Sao chép **nội dung** thư mục Codec vào root repository (gồm `.agents`, `.codec`, `AGENTS.md`).
Codex cần được hướng dẫn đọc `AGENTS.md` và `.agents/skills/codec/SKILL.md` khi bắt đầu tác vụ.

## Cấu hình trước khi sử dụng
Chỉnh `.codec/config.json`:
- `database.name`: mặc định `TAPortalSys` theo thông tin người dùng đã xác nhận.
- `deploy.targets`: PHẢI điền đường dẫn publish thực tế cho từng website; để trống nghĩa là KHÔNG publish.
- `git.main_branch`: `main`.
- `deploy.require_approval`: true; `git.merge_requires_approval`: true.

## Khởi tạo change
`python .codec/tools/codec.py new --feature CUSTOMER-MANAGEMENT --title "Bổ sung lọc khách hàng"`

## Xem trạng thái
`python .codec/tools/codec.py list`
`python .codec/tools/codec.py validate`

## Vòng đời
Intake -> Plan -> Implement -> Build/Test -> SQL handoff (KHÔNG execute) -> Publish to configured exact path (after approval) -> Verify -> Review -> Merge (after approval) -> Report.

Mỗi thay đổi có một `Change ID` duy nhất; một `Feature ID` có nhiều changes. History ghi bằng Markdown và JSON, commit vào Git. Không lưu mật khẩu, connection string có password hoặc dữ liệu cá nhân vào history.

**Lưu ý:** Các guard trong skill là hướng dẫn tác nghiệp, không phải sandbox bảo mật. Giới hạn quyền SQL/IIS của tài khoản chạy Codex vẫn cần được áp dụng.

## DEV/TEST MACHINE POLICY (2026-10-28)
- This machine is for development and testing, not a production deployment server.
- Codex must not inspect IIS, websites, application pools, bindings or physical paths; connect to or probe server configuration; request Administrator access; deploy or change IIS websites.
- Never run seed, ad-hoc mutating SQL or data-writing integration tests; never create databases or execute stored procedures with write effects. Migrations and their rollback scripts run only under `## AGENT MIGRATION EXECUTION`.
- Outside that procedure, only TAPortalSys read queries are allowed. Without read access, record NOT_VERIFIED and continue independent work.
- Author migration SQL files with a matching rollback script. SQL verification defaults to TEXT messages via PRINT; SELECT details only when diagnosing failures.

## Local publish policy — Checkpoint 3
- Publish every web project only to `<WORKSPACE_ROOT>\src\publish\deployed-<PROJECT_NAME>`; PROJECT_NAME is the csproj filename without extension. Never rename, move or timestamp the target, or publish into a project source directory.
- This is a local artifact directory, not an assumed IIS deployment. Do not inspect IIS before local publish; unreadable IIS configuration does not block local artifacts.
- Check project, Git, build/safe tests, target contents and write permission. Creating the exact conventional folder is authorized. Reject path aliases/reparse points escaping the target; never delete or overwrite data outside it.
- Local artifact publishing does not authorize server/IIS configuration or deployment. Report PUBLISHED separately from NOT_DEPLOYED, source commit SHA separately from artifact SHA256.
- Historical: Checkpoint 3 authorized TACustomerPortal.Web only. Superseded by the MULTI-AGENT CONTRACT below: which web projects are published is decided per task by impact analysis plus the operator's request. Any live deployment requires separate approval and operator-managed backup/replacement.

## AGENT MIGRATION EXECUTION (2026-11-03, operator approval)
Supersedes every earlier clause that forbids agents from executing migrations. It covers only files in `database/migrations/` and their rollback scripts in `database/scripts/`; seed, ad-hoc DML/DDL, data-writing tests and database creation stay forbidden.
- Target: production database `TAPortalSys` only, using the connection string from the operator's deployed `appsettings.json` (never copy, print, commit or log the credentials). Before every step, check `DB_NAME() = 'TAPortalSys'`; stop on mismatch.
- Run migrations one at a time, in numeric order. For each migration:
  1. Pre-check (read-only): run its verify script, record the result; if it is already applied, mark ALREADY_APPLIED and skip.
  2. Rollback script: a rollback script for that migration must exist in `database/scripts/`. If missing, author it, record it in the change, and run it only in the dry-run below before trusting it.
  3. Dry-run with rollback: in one session, `SET XACT_ABORT ON; BEGIN TRAN;` → run the migration → run the verify script → run the rollback script → re-run the verify script (must show not-applied) → `ROLLBACK`. Afterwards confirm `@@TRANCOUNT = 0` and that the pre-check result is unchanged. Run it so execution stops at the first error (e.g. `sqlcmd -b`) and put `IF @@TRANCOUNT = 0 RAISERROR('ABORT: transaction lost', 16, 1);` after every batch, because a script's own `ROLLBACK`/`COMMIT` or an XACT_ABORT error can end the wrapper transaction and let later batches auto-commit. Any error or FAIL ⇒ stop, status DRY_RUN_FAILED, re-run the pre-check to prove nothing was left behind, no real run.
  4. Gates: the mandatory gates (restore, Release build with 0 errors, all Safe* tests) must pass on the current HEAD.
  5. Real run: run the migration for real, then the verify script. All PASS ⇒ EXECUTED. Any error or FAIL ⇒ run the rollback script immediately, verify it, status ROLLED_BACK, and stop the remaining migrations.
- Stop and ask the operator instead of running when a migration: cannot run inside a transaction (`ALTER DATABASE`, `BACKUP`/`RESTORE`, full-text, `CREATE/DROP DATABASE`); drops a table or column, or deletes/updates existing rows; or when the latest full backup in `msdb.dbo.backupset` for `TAPortalSys` is older than 24 hours or cannot be read.
- Record per migration in the Codec change/report and `history.json` `migration_status`: pre-check, dry-run output, gate results, real-run output, final status (ALREADY_APPLIED / EXECUTED / DRY_RUN_FAILED / ROLLED_BACK) and UTC timestamps. Output follows the SQL output rule above; never record secrets or PII.
