# Change CODEC-WORKFLOW-CHG-20261031T205904Z-79AB8C

- Feature ID: `CODEC-WORKFLOW`
- Change ID: `CODEC-WORKFLOW-CHG-20261031T205904Z-79AB8C`
- Tiêu đề: Check in operator files and hand off work to another machine
- Trạng thái: CHECKED_IN / HANDOFF
- Agent: Claude Code (claude-opus-5-5)

## Yêu cầu
Operator yêu cầu check in và push toàn bộ dự án để làm tiếp ở máy khác.

## File
- MODIFY `database/scripts/004-verify-customer-portal.sql` (thay đổi của operator, giữ nguyên nội dung)
- ADD `docs/mockup.png`, `src/.claude/settings.local.json.backup-20261028-233347` (file của operator; không có secret)
- KHÔNG ADD `database/scripts/scene-image.png` theo quy định không lưu PII trong Git; chờ operator quyết định.

## Bàn giao sang máy khác
- Tiếp tục trên nhánh `feature/customer-portal-mvp01`; PR #7 chưa merge.
- Không có trên Git: `src/publish/`, `publish/`, `bin/`, `obj/`, `TestResults/`, `Codec-TAPortal-Workflow-v1.zip`, và `database/scripts/scene-image.png` (ảnh chân dung không liên quan dự án, giữ lại máy cũ). Artifact cần publish lại ở máy mới nếu cần; đường dẫn `.codec/config.json` cố định theo `D:\T.A\TAPortal\TAPortal`, nên đặt repo đúng đường dẫn này hoặc đổi config có phê duyệt.
- Khóa Tingee và connection string không nằm trong Git, phải cấu hình lại ở môi trường chạy.
- Database: migration 011 và 012 đã được operator xác nhận; verify PASS; cảnh báo còn lại là TA_User thuộc db_owner.
- Việc còn lại: test Tingee thật bằng `Tingee__Mode=Direct` (NOT_VERIFIED); deploy (NOT_DEPLOYED); worker xử lý webhook và đối soát; phân quyền linh hoạt; Zalo; hóa đơn điện tử; Master Merchant khi được kích hoạt.

## Test
dotnet restore: OK; Release build: 0 warning / 0 error; safe tests: 308 passed / 0 failed / 0 skipped; Codec tests: OK; codec validate: PASS; git diff --check: sạch.

## Publish
NOT_RUN: không đổi source ứng dụng.

## Git
Branch feature/customer-portal-mvp01; base b7837f007528b32e0323efbd5e9e92671449d802; source c0be494f4c00807b911b2d3c3ee6fc60b7d8d8c0.
