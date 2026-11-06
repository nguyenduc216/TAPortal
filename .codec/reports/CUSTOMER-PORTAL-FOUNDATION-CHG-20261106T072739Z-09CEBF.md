# CODEC DELIVERY REPORT — CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T072739Z-09CEBF

Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T072739Z-09CEBF`

1. Mục tiêu và kết quả thực tế: trang /pay — ảnh và mô tả khi chia sẻ link; hai nút "Chuyển khoản" mở ứng dụng ngân hàng (deeplink VietQR từ chính QR của đơn); lưu thông tin xuất hóa đơn chỉ cho đơn chưa có MST người mua và chỉ khi tra cứu được MST.
2. Danh sách file cập nhật: xem `.codec/changes/CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T072739Z-09CEBF.md`.
3. Database: không có migration.
4. Build/test: Release build 0 lỗi; Safe tests 479/479.
5. Publish: invoice DEPLOYED 2026-11-06 14:32 (+07) từ commit 0bb7fe1: artifact 68e4835a…75157313, backup C:\TADeploy\backups\invoice-20261106-143218, health 200.
6. Git: branch `feature/customer-portal-mvp01`, source commit 0bb7fe1; main fast-forward cùng change CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T073457Z-C2D04D.
7. Rủi ro/việc còn lại: xem change (deeplink do VietQR, Zalo lưu đệm bản xem trước).
8. Bằng chứng: SafeCustomerPayPageTests; log deploy.
