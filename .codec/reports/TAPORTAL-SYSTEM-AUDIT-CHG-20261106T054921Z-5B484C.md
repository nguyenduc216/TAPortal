# CODEC DELIVERY REPORT — TAPORTAL-SYSTEM-AUDIT-CHG-20261106T054921Z-5B484C

Feature ID: `TAPORTAL-SYSTEM-AUDIT`
Change ID: `TAPORTAL-SYSTEM-AUDIT-CHG-20261106T054921Z-5B484C`

1. Mục tiêu và kết quả thực tế: thông báo Success/Error của Admin portal và Customer Portal hiện thành toast cố định ở trên cùng, chính giữa màn hình (nút đóng, Escape, tự ẩn 5 s / 9 s, dừng khi rê chuột).
2. Danh sách file cập nhật: xem `.codec/changes/TAPORTAL-SYSTEM-AUDIT-CHG-20261106T054921Z-5B484C.md`.
3. Database: không có migration.
4. Build/test: Release build 0 lỗi; Safe tests 472/472 (audit Deploy-TARemote); kiểm tra giao diện trên trang thử dùng CSS/JS thật (1280px, 590px).
5. Publish: DEPLOYED 2026-11-06 (+07) từ commit 1a13b5d:
   - invoice 12:55: artifact 9473ec63…aa76da, gói C6980B2C…48F8C288, backup C:\TADeploy\backups\invoice-20261106-125456, health 200.
   - portal 12:58: artifact 62b22d31…7e40b0, gói 1D847A56…1DB4E5, backup C:\TADeploy\backups\portal-20261106-125818, health 200.
6. Git: branch `feature/customer-portal-mvp01`, source commit 1a13b5d; main fast-forward sau khi ghi nhận. Thay đổi chưa commit của phiên khác trong checkout không bị đưa vào.
7. Rủi ro/việc còn lại: chưa xem trên production khi đăng nhập thật (cần operator thao tác lưu/gửi thử).
8. Bằng chứng: SafeUiDesignSystemTests.FlashMessagesAreRaisedAsTopCentreToasts; log deploy.
