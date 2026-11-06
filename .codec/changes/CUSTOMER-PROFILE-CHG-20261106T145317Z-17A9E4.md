# Change CUSTOMER-PROFILE-CHG-20261106T145317Z-17A9E4

- Feature ID: `CUSTOMER-PROFILE`
- Change ID: `CUSTOMER-PROFILE-CHG-20261106T145317Z-17A9E4`
- Created UTC: 2026-11-06T14:53:17.098425+00:00
- Tiêu đề: Admin audit rows fail on the legacy dbo.AuditLogs schema (add Action/EntityName)
- Trạng thái: MIGRATED

## Yêu cầu và phạm vi
Operator (2026-11-06, ảnh chụp tab Tài khoản hệ thống): bấm Hiện/Sao chép mật khẩu báo "Không ghi được nhật ký audit (dbo.AuditLogs) nên không hiển thị mật khẩu."

Nguyên nhân (đọc schema production, chỉ đọc): `dbo.AuditLogs` trên TAPortalSys là bảng của hệ thống cũ (Id bigint identity, ModuleCode, FunctionCode, ActionCode, EntityType, EntityId, OldValues, NewValues, IpAddress, UserAgent, CorrelationId, CreatedAt), có trước migration 006/010 nên 010 không tạo lại. `TAPortal.Shared.AuditSql.Insert` ghi `Action` và `EntityName`, kiểm tra cột trước và trả SchemaMismatch, nên Admin chưa từng ghi được dòng audit nào (230 dòng hiện có đều của hệ thống cũ, 2026-10-17). Các thao tác khác vẫn chạy vì `Security:Audit:RequireDurableAudit=false`; xem mật khẩu luôn bắt buộc audit nên bị chặn. Login ứng dụng là db_owner (không phải lỗi quyền).

Cách sửa: thêm 2 cột nullable `Action nvarchar(100)`, `EntityName nvarchar(200)`; không đổi code (code khớp migration 006/010 của repo), không sửa dòng cũ.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `database/migrations/025-audit-logs-admin-columns.sql`
- ADD `database/scripts/039-verify-audit-logs-admin-columns.sql`
- ADD `database/scripts/040-rollback-audit-logs-admin-columns.sql` (từ chối khi đã có dòng audit ghi vào `Action`)

## Migration SQL
- Database: TAPortalSys
- Chạy bằng `deploy/windows/Invoke-TAMigrations.ps1 -Plan "025:039:040"`
- Execution: EXECUTED 2026-11-06T14:55:58Z: 025 (verify 039, rollback 040) - precheck NOT_APPLIED, rolled-back dry run PASS/NOT_APPLIED/trancount 0, latest full backup 1419 min old (< 24 h, no new backup), gates 481/481 Safe tests PASS, real run VERIFY_STATUS=PASS
- Log cục bộ (bị .gitignore): `.codec/reports/CUSTOMER-PROFILE-CHG-20261106T145317Z-17A9E4-migrations-20261106T145408Z.log`

## Test và kết quả
Gates trong script migration: restore, build Release, Safe tests 481/481 PASS.

## Publish
- NOT_APPLICABLE: không đổi ứng dụng.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: (hand-off)
- Merge: main (hand-off)

## Rủi ro, quyết định và việc còn lại
- Dòng audit cũ giữ `ActionCode`/`EntityType`; dòng mới của Admin/Customer Portal ghi `Action`/`EntityName`. Màn hình Tổng quan đọc `Action` nên chỉ hiện dòng mới.
- Có thể bật `Security__Audit__RequireDurableAudit=true` sau khi chạy `database/scripts/007-verify-audit-logs.sql` (quyết định của operator, runbook `docs/operations/durable-audit-enablement-runbook.md`).
- Chưa thử lại nút Hiện bằng tài khoản thật (agent không đăng nhập).
