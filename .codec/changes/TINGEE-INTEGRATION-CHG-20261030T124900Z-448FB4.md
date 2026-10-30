# Change TINGEE-INTEGRATION-CHG-20261030T124900Z-448FB4

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261030T124900Z-448FB4`
- Created UTC: 2026-10-30T12:49:00Z
- Tiêu đề: Fix collation conflict in diagnostic script 014
- Trạng thái: IMPLEMENTED / SQL_NOT_VERIFIED / PUBLISH_NOT_RUN
- Agent: Claude Code (claude-opus-5-5)

## Database
Operator chạy 014 gặp `Msg 468` ở lần so sánh `sys.database_permissions.permission_name` (collation `Latin1_General_CI_AS_KS_WS`) với cột của biến bảng (`SQL_Latin1_General_CP1_CI_AS`). Đã thêm `COLLATE DATABASE_DEFAULT` và dòng in user đang chạy. Script chỉ đọc, NOT_EXECUTED sau khi sửa; agent không chạy được nên chờ operator xác nhận.

## File
- MODIFY `database/scripts/014-diagnose-tingee-foundation.sql`

## Test
dotnet restore: OK; Release build: 0 error; safe tests: 308 passed; Codec tests: OK; codec validate: PASS; git diff --check: sạch. Script SQL chưa được chạy lại.

## Publish
NOT_RUN: chỉ sửa script SQL, không đổi source ứng dụng; artifact của change 05E4FE vẫn hiện hành.

## Git
Branch feature/customer-portal-mvp01; base f24133867acc447c2f685347929485308e3926b9; source a04dab999b24c3779607a0296eb946ff4e5d0deb; PR #7 chưa merge.
