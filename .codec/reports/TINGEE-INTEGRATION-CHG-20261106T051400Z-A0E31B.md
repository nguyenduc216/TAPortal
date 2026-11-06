# CODEC DELIVERY REPORT — TINGEE-INTEGRATION-CHG-20261106T051400Z-A0E31B

Feature ID: `TINGEE-INTEGRATION`
Change ID: `TINGEE-INTEGRATION-CHG-20261106T051400Z-A0E31B`

1. Mục tiêu và kết quả thực tế: gửi thử Email SMTP báo thành công nhưng Gmail không nhận. Nguyên nhân: System.Net.Mail không ghi Message-ID, Gmail từ chối sau khi máy chủ SMTP đã nhận. Đã thêm Message-ID cho mọi thư (kênh SMTP thông báo/gửi thử, email khôi phục mật khẩu Customer Portal).
2. Danh sách file cập nhật: xem `.codec/changes/TINGEE-INTEGRATION-CHG-20261106T051400Z-A0E31B.md`.
3. Database: không có migration. Audit deploy webhooks gặp migration 023 (change CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T044512Z-10B30D, EXECUTED 04:47:36Z) trong khoảng commit; đã đọc xác nhận trên production (InvoicePattern max_length 50) rồi mới dùng -MigrationsApplied.
4. Build/test: Release build 0 lỗi; Safe tests 471/471; test_codec OK; git diff --check OK.
5. Publish: DEPLOYED 2026-11-06 (+07) từ commit 7bb181a qua Deploy-TARemote.ps1:
   - webhooks 12:25: artifact e4f78a8f…042f56, gói B19E3CFF…AF5FCD5, backup C:\TADeploy\backups\webhooks-20261106-122540, health 200.
   - invoice 12:28: artifact 5ee21a94…662227, gói E49360A5…E739CCA, backup C:\TADeploy\backups\invoice-20261106-122753, health 200.
   - portal 12:30: artifact 8bd9e5b1…023ee8c, gói 62211A91…6EB1D2, backup C:\TADeploy\backups\portal-20261106-123014, health 200.
6. Git: branch `feature/customer-portal-mvp01`, source commit 7bb181a; main fast-forward sau khi ghi nhận.
7. Rủi ro/việc còn lại: chưa xác nhận bằng thư thật tới Gmail — operator gửi thử lại. Nếu vẫn không tới: đọc thư trả về trong hộp thư người gửi; cân nhắc DKIM + DMARC cho tên miền gửi.
8. Bằng chứng: SafeNotificationTests.EveryMailCarriesAMessageId; log deploy.
