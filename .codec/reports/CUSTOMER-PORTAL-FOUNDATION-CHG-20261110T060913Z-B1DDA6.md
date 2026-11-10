# CODEC DELIVERY REPORT — CUSTOMER-PORTAL-FOUNDATION-CHG-20261110T060913Z-B1DDA6

Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261110T060913Z-B1DDA6`

1. Mục tiêu và kết quả thực tế: trang thanh toán có nút tải ảnh mã QR kèm thông tin chuyển khoản; nút Chuyển khoản mở app ngân hàng hỗ trợ (MB, BIDV, VietinBank, ACB, OCB) ở màn hình chuyển khoản điền sẵn từ QR.
2. Danh sách file cập nhật: xem `.codec/changes/CUSTOMER-PORTAL-FOUNDATION-CHG-20261110T060913Z-B1DDA6.md`.
3. Database: không có migration (NOT_APPLICABLE).
4. Build/test: Release build 0 lỗi; Safe tests 622/622; kiểm tra trên trình duyệt.
5. Publish: invoice DEPLOYED 2026-11-10 06:38Z: source commit bd93fff, artifact ffe45e045849b41f5ee22483e6f804e57c0300e7e3215239206de1573e99cacd, gói 141BCD13FC3F13E96CD7D34D9BD103128D52FD9391DB3444A3DA8728F41EFE3D, backup C:\TADeploy\backups\invoice-20261110-133804, health 200.
6. Git: branch `feature/customer-portal-mvp01`, source commit bd93fff; main fast-forward.
7. Rủi ro/việc còn lại: ngân hàng ngoài danh sách điền sẵn chỉ mở app; chưa thử trên điện thoại thật.
8. Bằng chứng: test Safe; log deploy.
