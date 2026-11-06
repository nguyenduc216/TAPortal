# Change TINGEE-INTEGRATION-CHG-20261106T051400Z-A0E31B

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261106T051400Z-A0E31B`
- Created UTC: 2026-11-06T05:14:00.530222+00:00
- Tiêu đề: SMTP: them Message-ID de Gmail khong tu choi thu
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator (2026-11-06): Cài đặt thông báo trên Customer Portal báo "Gửi thử thành công" qua kênh Email SMTP của doanh nghiệp nhưng hộp thư Gmail không nhận được thư.

Nguyên nhân (đã kiểm chứng): System.Net.Mail không ghi header Message-ID (kiểm tra bằng pickup directory: chỉ có From/To/Date/Subject/MIME). Gmail từ chối thư thiếu Message-ID (550 5.7.1) sau khi máy chủ SMTP của doanh nghiệp đã nhận thư, nên ứng dụng thấy thành công còn thư bị trả về hộp thư người gửi. SPF của tên miền gửi đã cho phép máy chủ SMTP; tên miền chưa có DMARC.

Sửa: `MailIds.Stamp` thêm `Message-ID: <guid@tên-miền-người-gửi>` cho mọi thư gửi đi — kênh Email SMTP (thông báo + gửi thử) và email khôi phục mật khẩu của Customer Portal.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TAPortal.Integrations/Notifications/NotificationTransports.cs` (MailIds.Stamp, dùng trong SmtpAsync)
- MODIFY `src/TACustomerPortal.Web/Services/PasswordRecovery.cs` (SmtpEmailSender gọi MailIds.Stamp)
- MODIFY `tests/TACustomerPortal.Tests/SafeNotificationTests.cs` (EveryMailCarriesAMessageId)

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Không có migration.
- Execution: NOT_APPLICABLE

## Test và kết quả
- `dotnet build TAPortal.sln -c Release`: 0 lỗi. Safe tests 471/471 pass.

## Publish
- Thay đổi TAPortal.Integrations ⇒ publish lại portal, invoice, webhooks. Xem report.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: xem report
- PR:
- Merge: xem report
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Chưa xác nhận bằng thư thật tới Gmail: operator gửi thử lại sau deploy. Nếu vẫn không tới: xem thư trả về trong hộp thư người gửi; cân nhắc bật DKIM tại nhà cung cấp mail và thêm bản ghi DMARC cho tên miền gửi (việc DNS của operator).
- Admin portal (TAPortal.Web) dùng cùng transport cho kênh SYSTEM nên cũng được sửa.
