# Change TINGEE-INTEGRATION-CHG-20261115T011455Z-BC9386

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261115T011455Z-BC9386`
- Created UTC: 2026-11-15T01:14:55.012612+00:00
- Tiêu đề: Cai dat thong bao: tab Hoa don tu dong va Portal, kenh email rieng cho Portal
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator (2026-11-15, ảnh trang "Cài đặt thông báo"): "Phân chia rõ cài đặt cho hoadontudong và portal, thêm phần của portal, sử dụng tab".

Đã làm:
- Trang "Cài đặt thông báo" có 2 tab (`ta-tabs`): "Hóa đơn tự động" (kênh SYSTEM gửi cho doanh nghiệp: Zalo OA, Telegram chatbot, Email SMTP) và "Portal" (kênh cho nhân viên T.A: Email T.A Portal mới + Telegram nhóm nhân viên SYSTEM). Lưu/gửi thử quay lại đúng tab; bảng "Thông báo gần đây" lọc theo tab.
- Scope mới `PORTAL` (chỉ kênh SMTP, không chọn sự kiện): email tài khoản nhân viên (mật khẩu mới sau khi đổi) và bản tin theo dõi văn bản pháp luật đi qua kênh này; chưa bật thì dùng kênh Email SYSTEM như trước (`NotificationSettings.SendSystemEmailAsync`).
- Migration 055 (schema): CK_NC_Scope và CK_NO_Scope chấp nhận `PORTAL` (không có khách hàng). Verify 097, rollback 098.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `database/migrations/055-notification-portal-scope.sql`, `database/scripts/097-verify-notification-portal-scope.sql`, `database/scripts/098-rollback-notification-portal-scope.sql`
- MODIFY `src/TAPortal.Integrations/Notifications/{Notifications.cs,NotificationService.cs}`
- MODIFY `src/TAPortal.Web/Controllers/NotificationSettingsController.cs`, `src/TAPortal.Web/Views/NotificationSettings/Index.cshtml`
- MODIFY `tests/TACustomerPortal.Tests/{SafeCustomerWorkspaceTests.cs,SafeNotificationTests.cs}`

## Migration SQL (KHÔNG tự chạy)
- 055 (verify 097, rollback 098), chạy bằng `Invoke-TAMigrations.ps1 -Plan "055:097:098"`.

## Test và kết quả
- Release build 0 lỗi; Safe tests 776/776 (mới: StaffEmailsUseThePortalChannelFirst; cập nhật TAConfiguresItsOwnFourNotificationChannels theo 2 tab).

## Publish
- portal, invoice, webhooks (Integrations thay đổi). Xem report.

## Git
- Branch: feature/tx-sync
- Commit SHA: xem report
- PR:
- Merge: xem report
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Kênh Email T.A Portal cần operator nhập cấu hình SMTP và bật; đến lúc đó email nhân viên vẫn đi qua kênh Email của Hóa đơn tự động (tên người gửi "Hóa đơn tự động").
