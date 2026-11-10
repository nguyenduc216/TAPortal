# CODEC DELIVERY REPORT — CUSTOMER-PORTAL-FOUNDATION-CHG-20261110T070417Z-914962

Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261110T070417Z-914962`

1. Mục tiêu và kết quả thực tế: nút sao chép số tài khoản, số tiền, nội dung trên trang thanh toán; danh sách app ngân hàng tự cập nhật theo VietQR (12 giờ/lần).
2. Danh sách file cập nhật: xem `.codec/changes/CUSTOMER-PORTAL-FOUNDATION-CHG-20261110T070417Z-914962.md`.
3. Database: không có migration (NOT_APPLICABLE).
4. Build/test: Release build 0 lỗi; Safe tests 622/622; kiểm tra trên trình duyệt.
5. Publish: invoice DEPLOYED 2026-11-10 07:08Z: source commit 8972a5e, artifact 7debf0683733c1328617385d5d3d8fc2801f9c5175bb979f29a9427717c0ec6c, gói B0ABE7E8198A0A1C45E844884F625CFD5BF27515E14914D784675709CF381996, backup C:\TADeploy\backups\invoice-20261110-140808, health 200.
6. Git: branch `feature/customer-portal-mvp01`, source commit 8972a5e; main fast-forward.
7. Rủi ro/việc còn lại: nếu server không gọi được api.vietqr.io thì dùng danh sách dựng sẵn.
8. Bằng chứng: test Safe; log deploy.
