# Change TINGEE-INTEGRATION-CHG-20261105T044328Z-2BCDDC

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261105T044328Z-2BCDDC`
- Created UTC: 2026-11-05T04:43:28Z
- Tiêu đề: Notification channels: T.A system channels (Zalo OA, Telegram bot/group, SMTP) and company channels (Zalo OA, Telegram group, SMTP)
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator (2026-11-05), mục 8: "Thêm phần cài đặt kết nối zalo OA, telegram chatbot, telegram chatbot group, email smtp của hoadondientu để gửi thông tin cho khách hàng đang sử dụng trên hoadondientu. Và thêm cài đặt zalo OA, telegram chatbot group, email smtp cho khách hàng của hoadondientu gửi thông báo về cho khách hàng trên hệ thống của họ (lưu ý kỹ phần này nhé)".

Thiết kế:
- Hai cấp kênh trong `dbo.NotificationChannels`:
  - SYSTEM (T.A Admin → "Cài đặt thông báo"): Zalo OA (ZNS), Telegram chatbot (tin riêng), Telegram nhóm, SMTP. Gửi cho doanh nghiệp dùng hoadondientu tại địa chỉ doanh nghiệp tự khai ở portal (`dbo.CustomerNotificationRecipients`: email, SĐT Zalo, Telegram chat ID); Telegram nhóm = nhóm nhân viên T.A (mọi giao dịch cần xử lý).
  - CUSTOMER (Customer Portal → "Cài đặt thông báo", chỉ OWNER/ADMIN): Zalo OA, Telegram nhóm, SMTP của chính doanh nghiệp. Gửi cho khách hàng của doanh nghiệp (email / SĐT người mua trên đơn) khi đơn nhận tiền, và cho nhóm nhân viên của doanh nghiệp. Không có Telegram chatbot riêng ở cấp doanh nghiệp (CHECK `CK_NC_Scope`).
- Sự kiện: PAYMENT (đơn nhận tiền), ATTENTION (giao dịch chưa xử lý / cần xem lại).
- Gửi: `NotificationDispatcher` chạy trong TAPortal.Webhooks (15 giây/lần): lập tin cho giao dịch mới (`PaymentTransactions.NotifiedAt`, chỉ giao dịch trong 2 ngày gần nhất để bật kênh không gửi tràn tin cũ) vào `dbo.NotificationOutbox` (chống trùng bằng `EventKey`), gửi và thử lại tối đa 5 lần (chờ 2, 4, 8, 16 phút).

Bảo mật (theo yêu cầu "lưu ý kỹ"):
- Cô lập doanh nghiệp: mọi truy vấn kênh lọc theo (Scope, CustomerId) trong SQL; portal luôn truyền CustomerId của doanh nghiệp đang đăng nhập; kênh của doanh nghiệp chỉ dùng cho giao dịch của chính doanh nghiệp đó (test).
- Bí mật (mật khẩu SMTP, bot token, App secret / access / refresh token Zalo) được mã hóa bằng Windows DPAPI (LocalMachine, entropy riêng) trước khi lưu SQL; không bao giờ trả lại trang (chỉ hiện "Đã lưu — để trống để giữ nguyên"); để trống thì giữ, có ô "Xóa giá trị đã lưu".
- Chống SSRF: SMTP của doanh nghiệp phải là tên miền phân giải ra địa chỉ công khai (chặn loopback/private/link-local/CGNAT), cổng chỉ 587/25/2525; Telegram/Zalo chỉ gọi host cố định; bot token kiểm định dạng (không chèn được vào URL). HttpClient thông báo tắt log (URL Telegram chứa token).
- Tin gửi doanh nghiệp/người mua không chứa lý do kỹ thuật (có thể nêu tên đối tác); chỉ nhóm T.A thấy lý do gốc. Portal không nêu tên Tingee (test neutrality đã thêm `/notifications`, `/transactions`).
- Gửi thử: tối đa 5 lần/phút cho mỗi chủ kênh.
- Zalo: access token tự làm mới bằng refresh token (dùng một lần) — lưu cặp token mới theo `SecretVersion` (optimistic) để hai tiến trình không ghi đè nhau.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `database/migrations/020-notification-channels.sql`, `database/scripts/029-verify-notification-channels.sql`, `database/scripts/030-rollback-notification-channels.sql`
- ADD `src/TAPortal.Integrations/Notifications/Notifications.cs`, `NotificationTransports.cs`, `SqlNotificationStore.cs`, `NotificationService.cs`
- MODIFY `src/TAPortal.Integrations/TAPortal.Integrations.csproj` (System.Security.Cryptography.ProtectedData 8.0.0)
- MODIFY `src/TAPortal.Shared/UiIcon.cs` (icon bell, send, server)
- ADD `src/TACustomerPortal.Web/Controllers/NotificationsController.cs`, `Views/Notifications/Index.cshtml`; MODIFY `Program.cs`, `Views/Shared/_Layout.cshtml`
- ADD `src/TAPortal.Web/Controllers/NotificationSettingsController.cs`, `Views/NotificationSettings/Index.cshtml`; MODIFY `Program.cs`, `Views/Shared/_Layout.cshtml`
- MODIFY `src/TAPortal.Webhooks/Program.cs` (NotificationWorker)
- ADD `tests/TACustomerPortal.Tests/SafeNotificationTests.cs`; MODIFY `SafeCustomerPaymentsTests.cs`, `SafeCustomerWorkspaceTests.cs`

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script: `database/migrations/020-notification-channels.sql` (sau 019; chỉ tạo bảng mới)
- Verify / Rollback: `database/scripts/029-verify-notification-channels.sql` / `database/scripts/030-rollback-notification-channels.sql`
- Execution: EXECUTED 2026-11-05T05:03:28Z via `deploy/windows/Invoke-TAMigrations.ps1` (sqlcmd -E on ta-server): pre-check NOT_APPLIED → dry-run in one transaction (migration → verify PASS → rollback → verify NOT_APPLIED → ROLLBACK, @@TRANCOUNT=0) → pre-check again NOT_APPLIED → backup: latest full 1103 min old (< 24 h) → gates restore/build/Safe 416/416 PASS → real run → verify PASS. Log: `.codec/reports/TINGEE-INTEGRATION-CHG-20261105T042908Z-94446D-migrations-20261105T050148Z.log`.

## Test và kết quả
- Release build 0 lỗi; Safe tests 416/416 pass (planner theo đối tượng nhận, không dùng kênh doanh nghiệp khác, bí mật không hiện lại, xóa/giữ bí mật, chặn IP nội bộ, dispatcher thử lại + lưu token mới, trang portal chỉ OWNER/ADMIN, trang Admin 4 kênh).
- Gửi thật qua SMTP/Telegram/Zalo: NOT_VERIFIED (cần cấu hình thật, dùng nút "Gửi thử" sau deploy).

## Publish
- TAPortal.Webhooks, TAPortal.Web, TACustomerPortal.Web (Integrations + Shared đổi). Xem report.

## Git
- Branch: feature/customer-portal-mvp01
- Merge: PENDING_APPROVAL

## Rủi ro, quyết định và việc còn lại
- DPAPI LocalMachine: bí mật chỉ giải mã được trên máy chủ T.A (3 site cùng máy). Chuyển máy chủ ⇒ nhập lại bí mật.
- System.Net.Mail chỉ hỗ trợ STARTTLS (587/25/2525), không hỗ trợ SSL ngầm định 465.
- Zalo ZNS cần mẫu đã duyệt và có phí theo tin; tham số mẫu cấu hình bằng JSON với các biến {order_no} {amount} {paid} {total} {status} {company} {buyer} {time}.
- Telegram chatbot riêng: người nhận phải bấm Start với bot và khai chat ID của mình.
- Doanh nghiệp tự khai nơi nhận thông báo từ T.A (có thể khai sai người nhận; số tin giới hạn theo số giao dịch của chính họ).
