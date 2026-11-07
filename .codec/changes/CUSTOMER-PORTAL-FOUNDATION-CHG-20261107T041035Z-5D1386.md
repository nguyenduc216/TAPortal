# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261107T041035Z-5D1386

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261107T041035Z-5D1386`
- Created UTC: 2026-11-07T04:10:35.591028+00:00
- Tiêu đề: Drop the unused Customer Portal member permissions (migration 030)
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator (2026-11-07): phần database của tính năng phân quyền Customer Portal đã hoàn nguyên (change 9A2C1B) — "Xóa dữ liệu".

Sửa: migration 030 xóa cột `dbo.CustomerPortalMemberships.Permissions` và trả CK_CPM_Role về OWNER, ADMIN, VIEWER; từ chối nếu còn membership MEMBER hoặc có Permissions (production: 0 dòng). Operator đã đồng ý việc drop cột.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `database/migrations/030-drop-customer-member-permissions.sql`, `database/scripts/049-verify-drop-customer-member-permissions.sql`, `database/scripts/050-rollback-drop-customer-member-permissions.sql`

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- `030-drop-customer-member-permissions.sql` (verify 049, rollback 050 = thêm lại cột rỗng và MEMBER).
- Execution: xem report

## Test và kết quả
- Không đổi code ứng dụng; Safe tests 562/562.

## Publish
- Không cần (ứng dụng không còn dùng cột này từ 03432e8).

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: xem report
- PR:
- Merge: xem report
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Script verify 047 của 029 sẽ báo NOT_APPLIED sau 030 (đúng như mong đợi).
