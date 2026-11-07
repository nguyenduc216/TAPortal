# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261107T071821Z-4EA212

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261107T071821Z-4EA212`
- Created UTC: 2026-11-07T07:18:21.733229+00:00
- Tiêu đề: Ten nguoi gui email he thong: Hoa don tu dong
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator (2026-11-07): "bạn làm luôn phần này cho tôi đi" — tên người gửi email phía Hóa đơn tự động.

Kiểm tra (chỉ đọc): appsettings.json và web.config của site TA.Invoice không có mục Email/FromName, nên email đặt lại mật khẩu dùng mặc định trong code ("Hóa đơn tự động", change 03398E) — không cần sửa cấu hình server. Kênh thông báo SMTP hệ thống (dbo.NotificationChannels Scope=SYSTEM, đang tắt) có fromName "Hoá đơn tự động T.A" → đổi thành "Hóa đơn tự động" bằng migration 033. Kênh SMTP riêng của doanh nghiệp giữ tên của doanh nghiệp đó.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `database/migrations/033-system-email-sender-name.sql`, `database/scripts/055-verify-system-email-sender-name.sql`, `database/scripts/056-rollback-system-email-sender-name.sql`

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- `033-system-email-sender-name.sql` (verify 055, rollback 056): sửa một giá trị trong một dòng có sẵn (operator đồng ý trong chat).
- Execution: xem report

## Test và kết quả
- Gates của Invoke-TAMigrations.

## Publish
- Không cần (chỉ dữ liệu).

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: xem report
- PR:
- Merge: xem report
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Không có.
