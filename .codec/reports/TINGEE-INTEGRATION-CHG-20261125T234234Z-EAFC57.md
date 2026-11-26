# CODEC DELIVERY REPORT — TINGEE-INTEGRATION-CHG-20261125T234234Z-EAFC57

Feature ID: `TINGEE-INTEGRATION`
Change ID: `TINGEE-INTEGRATION-CHG-20261125T234234Z-EAFC57`

1. Mục tiêu và kết quả thực tế: thêm SoftDreams EasyInvoice làm nhà cung cấp hóa đơn thứ ba, gọi API trực tiếp. Login của doanh nghiệp được lưu mã hóa (DPAPI LocalMachine) theo quyết định của operator. Đã có: kết nối (kiểm tra login), nhập mẫu số/ký hiệu, xuất tự động/thủ công (ký server hoặc nháp), chống xuất trùng theo Ikey, tải PDF. Chưa thử với hệ thống thật của SoftDreams.
2. Danh sách file cập nhật: xem mục "File thay đổi" trong change.
3. Database `TAPortalSys`: `database/migrations/068-einvoice-softdreams-provider.sql` (sau 067), verify 123, rollback 124. EXECUTED 2026-11-26 11:46Z (VERIFY_STATUS=PASS, backup trước khi chạy đã verify).
4. Build/test: restore + Release build 0 lỗi; Safe tests 857/857 PASS (sau merge main); Codec tests OK; `git diff --check` OK.
5. Publish: DEPLOYED portal, invoice, webhooks từ commit 321c782; health 200 cả ba; không có lỗi trong Event Log.
6. Git: branch `feature/einvoice-softdreams` (130193f, merge main 321c782); merge vào main sau deploy.
7. Rủi ro/rollback/việc operator cần làm: thử với tài khoản test SoftDreams (môi trường demo) trên doanh nghiệp/đơn thử, chế độ xuất thủ công. Rollback app: `Deploy-TARemote.ps1 -Target <t> -Rollback`; rollback DB 124 từ chối khi đã có doanh nghiệp dùng SoftDreams.
8. Bằng chứng kiểm tra: kết quả test cục bộ (không có secrets).
