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
- Publish targets use the fixed convention below, explicitly authorized in Checkpoint 3. Separate approval remains required for live IIS overwrite.
- Reuse the current feature branch; create a branch only for independent new features or substantial work. Never reset/delete uncommitted work, force push, or merge main without user acceptance and approval. Record merge SHA after approved merge.
- Record objective, scope, technical decisions, changed files, migrations, build/test, publish, source commit SHA, merge SHA and remaining work in feature history/change/report. Never store secrets or PII.

## Publish convention ? Checkpoint 3 (2026-10-28)
- Every web project publishes only to `<WORKSPACE_ROOT>\publish\deployed-<PROJECT_NAME>`; PROJECT_NAME is the .csproj filename without extension. Paths are fixed, never timestamped, renamed or moved; never publish into source.
- Creating exactly that conventional folder is authorized. Before any write, inspect existing files, write permissions and all IIS site/application/virtual-directory physical paths (including parent/child overlap and path aliases). An unreadable IIS configuration is an unresolved safety preflight: STOP publish until the operator supplies verified mappings or access. Do not elevate automatically.
- No automatic publish to a live IIS directory. Stop for separate approval before overwriting any served folder; never clean it blindly. Back up existing artifacts/configuration before approved replacement.
- Publishing artifacts does not authorize IIS site/binding/app-pool changes, starts or recycles. Report PUBLISHED separately from NOT_DEPLOYED, with source commit SHA and artifact SHA256.
- This checkpoint authorizes only TACustomerPortal.Web. TAPortal.Web follows the same path convention for future authorized work, but is not published now.
