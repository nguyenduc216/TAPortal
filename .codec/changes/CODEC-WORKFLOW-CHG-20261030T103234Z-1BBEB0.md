# Change CODEC-WORKFLOW-CHG-20261030T103234Z-1BBEB0

- Feature ID: `CODEC-WORKFLOW`
- Change ID: `CODEC-WORKFLOW-CHG-20261030T103234Z-1BBEB0`
- Created UTC: 2026-10-30T10:32:34Z
- Tiêu đề: Publish targets under src/publish, per-project AGENTS.md, SQL text output rule
- Trạng thái: IMPLEMENTED / BUILD_VERIFIED
- Agent: Claude Code (claude-opus-5-5)

## Yêu cầu
Operator (2026-10-30): ghi đường dẫn publish vào AGENTS của từng dự án, gốc là `D:\T.A\TAPortal\TAPortal\src\publish` cộng thư mục theo tên dự án; SQL chỉ đọc hoặc trả kết quả mặc định là text, không trả dạng bảng khi không cần.

## Quyết định
- Thư mục đích giữ tiền tố `deployed-` theo quyết định thống nhất trước đó của operator: `src\publish\deployed-<PROJECT_NAME>`.
- Thư mục `publish\` cũ không bị xóa hay di chuyển; artifact và bản rollback cũ vẫn ở đó.
- `final-deployment-uat-runbook.md` giữ nguyên vì ghi lại lần publish cũ.

## File
- ADD `src/<Project>/AGENTS.md` cho TAPortal.Web, TACustomerPortal.Web, TAPortal.Webhooks, TAPortal.Shared, TAPortal.Integrations, TAPortal.DbMcp
- MODIFY `AGENTS.md`, `.agents/skills/codec/SKILL.md`, `.codec/README.md`, `.codec/config.json`, `.codec/tools/codec.py`, `.codec/tools/test_codec.py`, `docs/customer-portal-runbook.md`

## Test
dotnet restore: OK; Release build: 0 warning / 0 error; safe tests: 308 passed / 0 failed / 0 skipped; Codec unit tests: OK; codec validate: PASS; git diff --check: sạch.

## Git
Branch feature/customer-portal-mvp01; base 205febb3dde0933dc80c6df9fab6f8dd172f81f3; source 53dbe68cf9907ff3931fd593203435c15a180029; delivery SHA báo sau khi push; PR #7 chưa merge.
