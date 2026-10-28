# TAPortal — Codec operating contract

Trước mọi công việc sửa source, SQL, publish hoặc Git, đọc:
1. `.agents/skills/codec/SKILL.md`
2. `.codec/config.json`
3. `.codec/features/<FEATURE_ID>/README.md` nếu đã có
4. `.codec/changes/` các change gần nhất thuộc feature đó (theo index).

Bắt buộc xác định FEATURE_ID + CHANGE_ID trước khi sửa. Chỉ đọc lịch sử liên quan thay vì toàn bộ chat/repo; khi cần kiểm tra ảnh hưởng thì mở rộng sang mã nguồn phụ thuộc.

Không tự chạy migration hoặc sửa schema/data SQL qua bất kỳ kênh nào. Không tạo database mới. Không tự tạo publish destination khác cấu hình. Không merge main khi chưa có approval. Không báo cáo deploy thành công nếu chỉ `dotnet publish` local. Báo cáo commit SHA, deployment artifact SHA256 và trạng thái thực tế tách biệt.

## Installation policy additions (CODEC-WORKFLOW)
- Intake order: identify FEATURE_ID; read AGENTS.md, Codec skill and config; read feature README.md AND history.json; read recent related changes/reports; only then inspect required source. If history is missing, initialize it and record facts verified from source. Do not scan all chat/repository without need.
- CHANGE_ID format: FEATURE-ID-CHG-YYYYMMDDTHHMMSSZ-XXXXXX (UTC, unique). Preserve previous changes/reports/history entries; append new records, update only the current change record.
- SQL Server: `WIN-HSHL54Q184K\MSSQLSERVER2014`; ONLY database: `TAPortalSys`. No DEV/UAT/TEST or other databases, including integration-test databases. No automatic migration, DDL, DML, seed or rollback via any channel. Only author SQL files; operator runs them manually with DBeaver or SSMS.
- Before authoring migrations, check existing scripts to avoid duplicates. Every mutating migration must check `DB_NAME() = 'TAPortalSys'` and abort on mismatch. Document exact path, execution order, purpose, preconditions and verification instructions.
- Local artifact publish uses the fixed convention and DEV/TEST preflight below; IIS inspection is forbidden. Live deployment requires separate approval.
- Reuse the current feature branch; create a branch only for independent new features or substantial work. Never reset/delete uncommitted work, force push, or merge main without user acceptance and approval. Record merge SHA after approved merge.
- Record objective, scope, technical decisions, changed files, migrations, build/test, publish, source commit SHA, merge SHA and remaining work in feature history/change/report. Never store secrets or PII.

## DEV/TEST MACHINE POLICY (2026-10-28)
- This machine is for development and testing, not a production deployment server.
- Codex must not inspect IIS, websites, application pools, bindings or physical paths; connect to or probe server configuration; request Administrator access; deploy or change IIS websites.
- Never run migrations, seed, rollback, mutating SQL or data-writing integration tests; never create databases or execute stored procedures with write effects.
- Only TAPortalSys read queries using read-only permissions are allowed. Without read access, record NOT_VERIFIED and continue independent work.
- Author migration SQL files for operator review and manual execution only. SQL verification defaults to TEXT messages via PRINT; SELECT details only when diagnosing failures.

## Local publish policy — Checkpoint 3
- Publish every web project only to `<WORKSPACE_ROOT>\publish\deployed-<PROJECT_NAME>`; PROJECT_NAME is the csproj filename without extension. Never rename, move or timestamp the target, or publish into source.
- This is a local artifact directory, not an assumed IIS deployment. Do not inspect IIS before local publish; unreadable IIS configuration does not block local artifacts.
- Check project, Git, build/safe tests, target contents and write permission. Creating the exact conventional folder is authorized. Reject path aliases/reparse points escaping the target; never delete or overwrite data outside it.
- Local artifact publishing does not authorize server/IIS configuration or deployment. Report PUBLISHED separately from NOT_DEPLOYED, source commit SHA separately from artifact SHA256.
- Historical: Checkpoint 3 authorized TACustomerPortal.Web only. Superseded by the MULTI-AGENT CONTRACT below: which web projects are published is decided per task by impact analysis plus the operator's request. Any live deployment requires separate approval and operator-managed backup/replacement.

## MULTI-AGENT CONTRACT (2026-10-28) — applies to every AI coding agent
- Scope: Claude Code, OpenAI Codex and any other AI agent working in this repository follow the same AGENTS.md, Codec skill and `.codec/config.json`. Wherever older text says "Codex", read "every AI agent". No agent may reinterpret or skip Codec.
- Concurrency: before editing, record branch, HEAD, remote HEAD and `git status`. Never overwrite, stage, stash, reset or reformat uncommitted changes you did not make; leave them untouched and list them in the report. Do not switch or create branches without approval. Re-check HEAD/remote before commit and push; if another agent pushed meanwhile, review the impact first and stop before any push that could overwrite it. Never force push or merge main.
- Audit before fix: inspect the source, then classify findings CRITICAL / HIGH / MEDIUM / LOW. Separate a proven defect, a potential risk, a feature not yet implemented and a test-environment limitation. Never report a defect from speculation alone. Architecture changes, complex migrations or unclear business rules → BLOCKED / PENDING_APPROVAL; do not decide them yourself.
- Impact analysis: `src/TAPortal.Shared` is referenced by both `TAPortal.Web` (Admin) and `TACustomerPortal.Web` (Customer). Any change to Shared, shared configuration or dependencies must be evaluated against both web projects.
- Mandatory gates: `dotnet restore TAPortal.sln`; `dotnet build TAPortal.sln -c Release --no-restore` (0 errors); safe tests only (`FullyQualifiedName~SafePortalTests|FullyQualifiedName~SafeAdminCrudTests`; legacy SQL tests stay skipped); `python -m unittest discover -s .codec/tools -p test_codec.py`; `python .codec/tools/codec.py validate`; `git diff --check`.
- Publish after source change: when a task requires artifact delivery, every web project whose source or dependencies changed must be republished, and a Shared change means both web projects. Publish only to `<WORKSPACE_ROOT>\publish\deployed-<PROJECT_NAME>`, only after the gates pass, without `--no-build` against stale output. Before replacing an existing artifact, record its manifest SHA256 and keep a rollback copy outside the target. Never reuse an old hash for a new artifact. PUBLISHED only after the publish command exits 0; never DEPLOYED for a local artifact.
- Artifact SHA256 (per project): sort every artifact file by relative POSIX path (ordinal); for each file write the line `<lowercase sha256>  <relative/posix/path>` followed by LF; encode as UTF-8 without BOM; the artifact SHA256 is the SHA256 of those manifest bytes. Store the manifest under `.codec/reports/<CHANGE_ID>-<PROJECT_NAME>.sha256`, never inside the artifact. Also report file count, exit code, timestamp and source SHA.
- SHA separation: the source SHA is the commit containing the built and published application source. The delivery SHA is the final commit holding reports and metadata. A report never states its own commit SHA; give the delivery SHA in the hand-off message after push.
- Findings ledger: every audit report includes a table with columns ID | Project | Module | Severity | Problem | Root Cause | Fix | Files | Test | Status, where Status is FIXED / VERIFIED / OPEN / BLOCKED / PENDING_OPERATOR. VERIFIED requires executed evidence.
- Final report structure (identical for Claude and Codex): A. Identification, B. Audit Summary, C. Customer Portal Account Management, D. Admin CRUD, E. Changed Files, F. Database, G. Build & Tests, H. Admin Publish, I. Customer Publish, J. Security and Acceptance, K. Git Delivery, L. Operator Handoff. A section that does not apply states NOT_APPLICABLE or NOT_RUN with the reason.
- Database and server limits are unchanged: no migration, seed, rollback, DDL/DML, data-writing tests or writing stored procedures; migration files only, guarded by `DB_NAME() = 'TAPortalSys'`, status NOT_EXECUTED until the operator confirms. No IIS or server inspection on this DEV/TEST machine.
