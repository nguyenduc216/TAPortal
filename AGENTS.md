# TAPortal — Codec operating contract

Trước mọi công việc sửa source, SQL, publish hoặc Git, đọc:
1. `.agents/skills/codec/SKILL.md`
2. `.codec/config.json`
3. `.codec/features/<FEATURE_ID>/README.md` nếu đã có
4. `.codec/changes/` các change gần nhất thuộc feature đó (theo index).

Bắt buộc xác định FEATURE_ID + CHANGE_ID trước khi sửa. Chỉ đọc lịch sử liên quan thay vì toàn bộ chat/repo; khi cần kiểm tra ảnh hưởng thì mở rộng sang mã nguồn phụ thuộc.

Chỉ được chạy migration theo đúng quy trình ở mục `## AGENT MIGRATION EXECUTION` (dry-run có rollback trước, rồi mới chạy thật); ngoài quy trình đó không tự sửa schema/data SQL qua bất kỳ kênh nào. Không tạo database mới. Không tự tạo publish destination khác cấu hình. Không merge main khi chưa có approval. Không báo cáo deploy thành công nếu chỉ `dotnet publish` local. Báo cáo commit SHA, deployment artifact SHA256 và trạng thái thực tế tách biệt.

## Installation policy additions (CODEC-WORKFLOW)
- Intake order: identify FEATURE_ID; read AGENTS.md, Codec skill and config; read feature README.md AND history.json; read recent related changes/reports; only then inspect required source. If history is missing, initialize it and record facts verified from source. Do not scan all chat/repository without need.
- CHANGE_ID format: FEATURE-ID-CHG-YYYYMMDDTHHMMSSZ-XXXXXX (UTC, unique). Preserve previous changes/reports/history entries; append new records, update only the current change record.
- SQL Server: `WIN-HSHL54Q184K\MSSQLSERVER2014`; ONLY database: `TAPortalSys`. No DEV/UAT/TEST or other databases, including integration-test databases. No DDL, DML, seed or rollback via any channel, except migration files executed under `## AGENT MIGRATION EXECUTION`; the operator may still run any SQL manually with DBeaver or SSMS.
- Before authoring migrations, check existing scripts to avoid duplicates. Every mutating migration must check `DB_NAME() = 'TAPortalSys'` and abort on mismatch. Document exact path, execution order, purpose, preconditions and verification instructions.
- Local artifact publish uses the fixed convention and DEV/TEST preflight below; IIS inspection is forbidden. Live deployment requires separate approval.
- Reuse the current feature branch; create a branch only for independent new features or substantial work. Never reset/delete uncommitted work, force push, or merge main without user acceptance and approval. Record merge SHA after approved merge.
- Record objective, scope, technical decisions, changed files, migrations, build/test, publish, source commit SHA, merge SHA and remaining work in feature history/change/report. Never store secrets or PII.

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

## MULTI-AGENT CONTRACT (2026-10-28) — applies to every AI coding agent
- Scope: Claude Code, OpenAI Codex and any other AI agent working in this repository follow the same AGENTS.md, Codec skill and `.codec/config.json`. Wherever older text says "Codex", read "every AI agent". No agent may reinterpret or skip Codec.
- Concurrency: before editing, record branch, HEAD, remote HEAD and `git status`. Never overwrite, stage, stash, reset or reformat uncommitted changes you did not make; leave them untouched and list them in the report. Do not switch or create branches without approval. Re-check HEAD/remote before commit and push; if another agent pushed meanwhile, review the impact first and stop before any push that could overwrite it. Never force push or merge main.
- Audit before fix: inspect the source, then classify findings CRITICAL / HIGH / MEDIUM / LOW. Separate a proven defect, a potential risk, a feature not yet implemented and a test-environment limitation. Never report a defect from speculation alone. Architecture changes, complex migrations or unclear business rules → BLOCKED / PENDING_APPROVAL; do not decide them yourself.
- Impact analysis: `src/TAPortal.Shared` is referenced by both `TAPortal.Web` (Admin) and `TACustomerPortal.Web` (Customer). Any change to Shared, shared configuration or dependencies must be evaluated against both web projects.
- Mandatory gates: `dotnet restore TAPortal.sln`; `dotnet build TAPortal.sln -c Release --no-restore` (0 errors); safe tests only (`FullyQualifiedName~TACustomerPortal.Tests.Safe` (every Safe* class); legacy SQL tests stay skipped); `python -m unittest discover -s .codec/tools -p test_codec.py`; `python .codec/tools/codec.py validate`; `git diff --check`.
- Publish after source change: when a task requires artifact delivery, every web project whose source or dependencies changed must be republished, and a Shared change means both web projects. Publish only to `<WORKSPACE_ROOT>\src\publish\deployed-<PROJECT_NAME>`, only after the gates pass, without `--no-build` against stale output. Before replacing an existing artifact, record its manifest SHA256 and keep a rollback copy outside the target. Never reuse an old hash for a new artifact. PUBLISHED only after the publish command exits 0; never DEPLOYED for a local artifact.
- Artifact SHA256 (per project): sort every artifact file by relative POSIX path (ordinal); for each file write the line `<lowercase sha256>  <relative/posix/path>` followed by LF; encode as UTF-8 without BOM; the artifact SHA256 is the SHA256 of those manifest bytes. Store the manifest under `.codec/reports/<CHANGE_ID>-<PROJECT_NAME>.sha256`, never inside the artifact. Also report file count, exit code, timestamp and source SHA.
- SHA separation: the source SHA is the commit containing the built and published application source. The delivery SHA is the final commit holding reports and metadata. A report never states its own commit SHA; give the delivery SHA in the hand-off message after push.
- Findings ledger: every audit report includes a table with columns ID | Project | Module | Severity | Problem | Root Cause | Fix | Files | Test | Status, where Status is FIXED / VERIFIED / OPEN / BLOCKED / PENDING_OPERATOR. VERIFIED requires executed evidence.
- Final report structure (identical for Claude and Codex): A. Identification, B. Audit Summary, C. Customer Portal Account Management, D. Admin CRUD, E. Changed Files, F. Database, G. Build & Tests, H. Admin Publish, I. Customer Publish, J. Security and Acceptance, K. Git Delivery, L. Operator Handoff. A section that does not apply states NOT_APPLICABLE or NOT_RUN with the reason.
- Database and server limits: no seed, ad-hoc DDL/DML, data-writing tests or writing stored procedures. Migration files are guarded by `DB_NAME() = 'TAPortalSys'` and stay NOT_EXECUTED until executed under `## AGENT MIGRATION EXECUTION` or confirmed by the operator. No IIS or server inspection on this DEV/TEST machine.

## PUBLISH TARGETS AND SQL OUTPUT (2026-10-30, operator decision)
- Publish root: `D:\T.A\TAPortal\TAPortal\src\publish`, ignored by Git. Targets: `deployed-TAPortal.Web`, `deployed-TACustomerPortal.Web`, `deployed-TAPortal.Webhooks`. Each project's own `AGENTS.md` states its path. The older `<WORKSPACE_ROOT>\publish` folder holds earlier artifacts and rollback copies; never publish there again and do not delete it without operator approval. Rollback copies of artifacts go to `<WORKSPACE_ROOT>\src\publish\rollback\<CHANGE_ID>\<PROJECT_NAME>`.
- Libraries (`TAPortal.Shared`, `TAPortal.Integrations`) and the `TAPortal.DbMcp` tool have no publish target here. A change to `TAPortal.Integrations` requires republishing `TAPortal.Web`, `TACustomerPortal.Web` and `TAPortal.Webhooks`.
- SQL output rule: every read-only, verify or diagnostic SQL that an agent runs, authors or hands to the operator returns TEXT by default, using `PRINT` lines with a clear PASS/FAIL/WARN/INFO prefix, or one aliased text column when the channel accepts only a single SELECT. Do not return a result grid unless row-level detail is really needed for a failure that text cannot summarize; then state why. T-SQL must run on SQL Server 2014 (no `STRING_AGG`). Never print secrets or PII.

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
