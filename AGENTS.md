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

## Local publish policy ? Checkpoint 3
- Publish every web project only to `<WORKSPACE_ROOT>\publish\deployed-<PROJECT_NAME>`; PROJECT_NAME is the csproj filename without extension. Never rename, move or timestamp the target, or publish into source.
- This is a local artifact directory, not an assumed IIS deployment. Do not inspect IIS before local publish; unreadable IIS configuration does not block local artifacts.
- Check project, Git, build/safe tests, target contents and write permission. Creating the exact conventional folder is authorized. Reject path aliases/reparse points escaping the target; never delete or overwrite data outside it.
- Local artifact publishing does not authorize server/IIS configuration or deployment. Report PUBLISHED separately from NOT_DEPLOYED, source commit SHA separately from artifact SHA256.
- Checkpoint 3 authorizes TACustomerPortal.Web only; TAPortal.Web is not published. Any future live deployment requires separate approval and operator-managed backup/replacement.
