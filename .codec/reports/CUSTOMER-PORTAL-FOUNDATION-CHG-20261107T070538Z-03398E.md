# CODEC DELIVERY REPORT — CUSTOMER-PORTAL-FOUNDATION-CHG-20261107T070538Z-03398E

Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261107T070538Z-03398E`

1. Mục tiêu và kết quả thực tế: Customer Portal xưng là "Hóa đơn tự động"; chỉ còn dòng "Sản phẩm của Tư vấn giải pháp T.A" cuối sidebar, link https://tuvangiaiphapta.vn.
2. Danh sách file cập nhật: xem `.codec/changes/CUSTOMER-PORTAL-FOUNDATION-CHG-20261107T070538Z-03398E.md`.
3. Database: không có migration.
4. Build/test: Release build 0 lỗi; Safe tests 594/594 (cả trong audit deploy).
5. Publish: invoice DEPLOYED 14:11 (+07): source commit 1bdb338, artifact 0924e35f6c02c379158a83a9d046db255db27a3a889e3f33d9dae6203f43cf79, gói F779AFACF0F5057725FBE3028131EB63CBF8A53DA141BB3BAE08F8C1FE5DC7D9, backup C:\TADeploy\backups\invoice-20261107-141156, health 200.
6. Git: branch `feature/customer-portal-mvp01`, source commit 1bdb338; main fast-forward.
7. Rủi ro/việc còn lại: tên người gửi email đặt lại mật khẩu trên server do appsettings của server quyết định (operator kiểm tra PasswordRecovery:FromName).
8. Bằng chứng: SafeCustomerBrandTests; log deploy.
