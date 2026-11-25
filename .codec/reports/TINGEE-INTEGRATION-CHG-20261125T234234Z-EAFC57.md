# CODEC DELIVERY REPORT — TINGEE-INTEGRATION-CHG-20261125T234234Z-EAFC57

Feature ID: `TINGEE-INTEGRATION`
Change ID: `TINGEE-INTEGRATION-CHG-20261125T234234Z-EAFC57`

1. Mục tiêu và kết quả thực tế: thêm SoftDreams EasyInvoice làm nhà cung cấp hóa đơn thứ ba, gọi API trực tiếp. Login của doanh nghiệp được lưu mã hóa (DPAPI LocalMachine) theo quyết định của operator. Đã có: kết nối (kiểm tra login), nhập mẫu số/ký hiệu, xuất tự động/thủ công (ký server hoặc nháp), chống xuất trùng theo Ikey, tải PDF. Chưa thử với hệ thống thật của SoftDreams.
2. Danh sách file cập nhật: xem mục "File thay đổi" trong change.
3. Database `TAPortalSys`: `database/migrations/068-einvoice-softdreams-provider.sql` (sau 067), verify 123, rollback 124. NOT_EXECUTED.
4. Build/test: restore + Release build 0 lỗi; Safe tests 856/856 PASS; Codec tests OK; `git diff --check` OK.
5. Publish: NOT_PUBLISHED, NOT_DEPLOYED.
6. Git: branch `feature/einvoice-softdreams`; merge main PENDING_APPROVAL.
7. Rủi ro/rollback/việc operator cần làm: xin SoftDreams tài khoản demo để thử; quyết định chạy migration 068 và deploy portal/invoice/webhooks. Rollback 124 từ chối khi đã có doanh nghiệp dùng SoftDreams.
8. Bằng chứng kiểm tra: kết quả test cục bộ (không có secrets).
