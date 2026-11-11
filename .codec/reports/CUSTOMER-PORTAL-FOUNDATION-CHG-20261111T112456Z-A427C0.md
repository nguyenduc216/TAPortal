# CODEC DELIVERY REPORT — CUSTOMER-PORTAL-FOUNDATION-CHG-20261111T112456Z-A427C0

Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261111T112456Z-A427C0`

1. Mục tiêu và kết quả thực tế: chi tiết đơn hiển thị đúng trên điện thoại (không tràn ngang, bảng hàng hóa dạng thẻ, link QR xếp dòng); đổi thời hạn / tạo lại QR cho đơn đang mở (QR mới thay QR cũ, link khách giữ nguyên).
2. Danh sách file cập nhật: xem `.codec/changes/CUSTOMER-PORTAL-FOUNDATION-CHG-20261111T112456Z-A427C0.md`.
3. Database: không có migration (NOT_APPLICABLE); dùng cột `QrExpireMinutes` đã có.
4. Build/test: Release build 0 lỗi; Safe tests 625/625; codec tests OK; codec validate chỉ báo đường dẫn publish khác workspace operator (đã biết, script deploy coi là cảnh báo); Playwright 360 px.
5. Publish: invoice DEPLOYED 2026-11-11 11:34Z: source commit 77e4ec7 (gói từ faa76c4), artifact ce8fe1db79247ef82566016f93e788cd855a8cc5aee2800579bca1942eae3d7e, gói 92ACA20EAEA2DAC276BE8DC7B975F633CC3BEDAB0F9A426DAADFF2C1089B20A2, backup C:\TADeploy\backups\invoice-20261111-183405, health 200.
6. Git: branch `feature/customer-portal-mvp01`, source commit 77e4ec7; merge vào main.
7. Rủi ro/việc còn lại: ngân hàng ngoài MB/BIDV/VietinBank không xóa được QR động cũ; tiền vào QR cũ vẫn ghi nhận cho đơn, chuyển trùng sẽ báo "Trả dư".
8. Bằng chứng: test Safe; log deploy; ảnh chụp 360 px.
