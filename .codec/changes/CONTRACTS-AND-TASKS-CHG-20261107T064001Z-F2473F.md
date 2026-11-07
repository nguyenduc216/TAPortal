# Change CONTRACTS-AND-TASKS-CHG-20261107T064001Z-F2473F

- Feature ID: `CONTRACTS-AND-TASKS`
- Change ID: `CONTRACTS-AND-TASKS-CHG-20261107T064001Z-F2473F`
- Created UTC: 2026-11-07T06:40:01.018885+00:00
- Tiêu đề: Vai tro mau Nhan vien va Quan ly cho cong viec va hop dong
- Trạng thái: DEPLOYED

## Yêu cầu và phạm vi
Operator (2026-11-07): "Tạo vai trò mẫu" — hai vai trò cho module Công việc / Hợp đồng (change 50D5E9):
- STAFF "Nhân viên": TASKS.VIEW, TASKS.CREATE.
- MANAGER "Quản lý": mọi quyền TASKS.* và CONTRACTS.* của migration 031.
Chỉ thêm dòng mới qua migration (không sửa tay dữ liệu); vai trò vẫn sửa được trên "Vai trò & phân quyền" (SYS_ADMIN). Không gán cho tài khoản nào.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `database/migrations/032-sample-work-roles.sql`, `database/scripts/053-verify-sample-work-roles.sql`, `database/scripts/054-rollback-sample-work-roles.sql`

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- `032-sample-work-roles.sql` (verify 053, rollback 054; marker A0000032-…; rollback từ chối khi vai trò đã gán cho người dùng).
- Execution: EXECUTED 2026-11-07T06:42:42Z

## Test và kết quả
- Không đổi code ứng dụng; gates của Invoke-TAMigrations.

## Publish
- Không cần (chỉ dữ liệu).

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: d5e532b
- PR:
- Merge: fast-forward main
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Operator cần gán vai trò cho từng tài khoản nhân viên và liên kết tài khoản với hồ sơ nhân viên.
