# CODEC DELIVERY REPORT — CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T145102Z-9F4A81

Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T145102Z-9F4A81`

1. Mục tiêu và kết quả thực tế: logo Customer Portal vẽ lại thành vector sạch (logo.svg), dùng ở header; favicon và ảnh xem trước khi chia sẻ link dựng lại từ logo mới.
2. Danh sách file cập nhật: xem `.codec/changes/CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T145102Z-9F4A81.md`.
3. Database: không có migration.
4. Build/test: Release build 0 lỗi; Safe tests 481/481 (cả trong audit deploy).
5. Publish: invoice DEPLOYED 2026-11-06 21:55 (+07) từ commit 0037f2a: artifact e83c88c18eaee2f94d357a8d0d4c504dc27a149f8b438999f170b0506ab24989 (72 file), gói 03D1B577E6A953C7CE1FCCE6EB366EAA209AD4C6E2CEDDB02392DD2582B56912, backup C:\TADeploy\backups\invoice-20261106-215534, health 200. Kiểm tra trực tiếp: /images/logo.svg, /images/og-share.png, /images/favicon-32x32.png trả 200.
6. Git: branch `feature/customer-portal-mvp01`, source commit 0037f2a; main fast-forward.
7. Rủi ro/việc còn lại: Zalo/Facebook và trình duyệt có thể còn lưu đệm ảnh cũ một thời gian.
8. Bằng chứng: SafeUiDesignSystemTests.TheCustomerPortalShowsItsVectorLogo; log deploy.
