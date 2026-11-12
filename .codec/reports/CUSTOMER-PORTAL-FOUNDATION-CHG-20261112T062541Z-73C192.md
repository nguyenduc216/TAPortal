# CODEC DELIVERY REPORT — CUSTOMER-PORTAL-FOUNDATION-CHG-20261112T062541Z-73C192

Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261112T062541Z-73C192`

1. Mục tiêu và kết quả thực tế: đăng ký cho cá nhân (MST không bắt buộc), mật khẩu tự sinh gửi qua email theo mẫu có logo, mẫu email và điều khoản dịch vụ soạn trên TA Portal, popup điều khoản, dòng "Sản phẩm của Tư vấn giải pháp T.A" dưới cùng trang đăng nhập/đăng ký.
2. Danh sách file cập nhật: xem `.codec/changes/CUSTOMER-PORTAL-FOUNDATION-CHG-20261112T062541Z-73C192.md`.
3. Database: migration 036 EXECUTED (xem change).
4. Build/test: Release build 0 lỗi; Safe tests 663/663.
5. Publish: invoice DEPLOYED 2026-11-12T06:33:31Z: artifact 908ded7123bd655c47e402016e0826708f9c30318041245e5c3e988299006096, gói B034684FB890F59B704A323D01848C44E2F7A79B9F568CE358788123E02DC081, backup C:\TADeploy\backups\invoice-20261112-133139, health 200. portal DEPLOYED: artifact 913a2f40766c39bc421aca4a1b7aaaa8cae2a1d7608e578ad85ed54f007be8c2, gói 63A03ACEDB4CB7C8A7F796EC1F5FD634307DBDA46498ED9BA0B6AB91DF2BE072, backup C:\TADeploy\backups\portal-20261112-133328, health 200. Source commit 0f5c9d4.
6. Git: branch `feature/signup-individual`, source commit 0f5c9d4; main fast-forward.
7. Rủi ro/việc còn lại: điều khoản còn chỗ trống cần operator điền và luật sư rà soát; email thông báo thanh toán chưa dùng mẫu.
8. Bằng chứng: test Safe; log migration và deploy; trang thật.
