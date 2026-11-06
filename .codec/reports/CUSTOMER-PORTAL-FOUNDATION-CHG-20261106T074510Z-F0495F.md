# CODEC DELIVERY REPORT — CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T074510Z-F0495F

Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T074510Z-F0495F`

1. Mục tiêu và kết quả thực tế: danh sách Đơn hàng & QR có cột "Hóa đơn" (đã xuất kèm số / đang xuất / lỗi kèm lý do / chưa xuất kèm tự xuất hay xuất tay).
2. Danh sách file cập nhật: xem `.codec/changes/CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T074510Z-F0495F.md`.
3. Database: không có migration.
4. Build/test: Release build 0 lỗi; Safe tests 480/480.
5. Publish: invoice DEPLOYED 2026-11-06 14:48 (+07) từ commit 1329373: artifact 66ce89cd…f7bda040, backup C:\TADeploy\backups\invoice-20261106-144830, health 200.
6. Git: branch `feature/customer-portal-mvp01`, source commit 1329373; main fast-forward sau khi ghi nhận.
7. Rủi ro/việc còn lại: không có. (Ghi chú vận hành: lỗi ký số CallSignServiceFail của DH261106-0001 do tài khoản MISA eSign còn gắn chứng thư số đã hết hạn 14/10/2026 — doanh nghiệp xử lý trên MISA rồi xuất lại.)
8. Bằng chứng: SafeCustomerPaymentsTests.TheOrderListShowsTheInvoiceStatus; log deploy.
