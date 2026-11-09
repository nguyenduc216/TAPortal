# CODEC DELIVERY REPORT — CUSTOMER-PORTAL-FOUNDATION-CHG-20261109T162008Z-1FF4B8

Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261109T162008Z-1FF4B8`

1. Mục tiêu và kết quả thực tế: Danh mục sản phẩm (ảnh, giá gồm VAT, trạng thái), chọn sản phẩm khi tạo đơn, lưu giá riêng theo khách khi người dùng đồng ý.
2. Danh sách file cập nhật: xem `.codec/changes/CUSTOMER-PORTAL-FOUNDATION-CHG-20261109T162008Z-1FF4B8.md`.
3. Database: migration 034 EXECUTED qua Invoke-TAMigrations (dry-run có rollback trước); log trong `.codec/reports/CUSTOMER-PORTAL-FOUNDATION-CHG-20261109T162008Z-1FF4B8-migrations-*.log`.
4. Build/test: Release build 0 lỗi; Safe tests 622/622; trang thử trên trình duyệt.
5. Publish: invoice DEPLOYED 2026-11-09 23:26 (UTC+7): source commit 7fbdc2c, artifact 0b381bf21106f6504edf3ee230206be00b25771f7ad5be74ef5218d4fc81139b, gói 2AAFA4BA3C364D5CDBF6191FC6F89ECE14BF5DD596AA6D53FB555A192E182520, backup C:\TADeploy\backups\invoice-20261109-232629, health 200.
6. Git: branch `feature/customer-portal-mvp01`, source commit 7fbdc2c; main fast-forward.
7. Rủi ro/việc còn lại: hóa đơn điện tử có thể lệch 1–2 đồng với dòng gồm VAT số lượng > 1; chưa xác nhận trên production bằng tài khoản thật.
8. Bằng chứng: test Safe; log migration; log deploy.
