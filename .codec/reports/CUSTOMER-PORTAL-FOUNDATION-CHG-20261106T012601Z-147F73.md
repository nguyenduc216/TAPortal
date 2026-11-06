# CODEC DELIVERY REPORT — CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T012601Z-147F73

Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T012601Z-147F73`

1. Mục tiêu và kết quả thực tế: Customer Portal — trang Hóa đơn điện tử (kết nối MISA meInvoice, mẫu hóa đơn, bật xuất tự động, email người mua, xem thử nháp); site webhook tự xuất hóa đơn MISA cho đơn QR ngay khi thanh toán đủ; trang đơn hàng hiện hóa đơn, xuất tay, mở PDF.
2. Danh sách file cập nhật: xem `.codec/changes/CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T012601Z-147F73.md`.
3. Database `TAPortalSys`: migration 022-customer-einvoice.sql EXECUTED 2026-11-06T01:28:34Z qua Invoke-TAMigrations (pre-check NOT_APPLIED; dry-run PASS → rollback NOT_APPLIED, trancount 0; backup gần nhất 612 phút; gates 467/467; real run VERIFY_STATUS=PASS). Log local (gitignored): `.codec/reports/CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T012601Z-147F73-migrations-20261106T012702Z.log`.
4. Build/test: Release build 0 lỗi; Safe tests 467/467; test_codec OK; git diff --check OK.
5. Publish: xem phần deploy bên dưới.
6. Git: branch `feature/customer-portal-mvp01`, source commit 71cb50a.
7. Rủi ro/việc còn lại: chưa thử với tài khoản MISA thật; chưa trừ hạn mức hóa đơn của gói dịch vụ. Rollback: `Deploy-TARemote.ps1 -Target invoice|webhooks -Rollback`; script 034 chỉ khi chưa có cài đặt/mã hóa đơn.
8. Bằng chứng: SafeCustomerEInvoiceTests, log migration.
