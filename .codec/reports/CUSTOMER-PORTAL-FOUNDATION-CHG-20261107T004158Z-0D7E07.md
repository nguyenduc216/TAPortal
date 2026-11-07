# CODEC DELIVERY REPORT — CUSTOMER-PORTAL-FOUNDATION-CHG-20261107T004158Z-0D7E07

Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261107T004158Z-0D7E07`

1. Mục tiêu và kết quả thực tế: trang Hóa đơn điện tử cảnh báo cần HSM của MISA và cho chọn: đã mua HSM (ký số tự động, lưu xác nhận), chưa mua HSM (xuất nháp, duyệt thủ công), không xuất tự động. Chế độ nháp chỉ tạo hóa đơn nháp.
2. Danh sách file cập nhật: xem `.codec/changes/CUSTOMER-PORTAL-FOUNDATION-CHG-20261107T004158Z-0D7E07.md`.
3. Database: migration 027-einvoice-issue-mode.sql (verify 043, rollback 044).
   - Pre-check 2026-11-07T00:42:47Z: VERIFY_STATUS=NOT_APPLIED.
   - Dry-run (migration → verify PASS → rollback → verify NOT_APPLIED → ROLLBACK, @@TRANCOUNT=0) 00:42:48Z: PASS; pre-check lại NOT_APPLIED.
   - Backup: bản full gần nhất 51 phút (trong 24 giờ), không cần backup thêm.
   - Gates restore/build/Safe tests 487/487: PASS.
   - Real run 00:44:31Z: VERIFY_STATUS=PASS ⇒ EXECUTED.
4. Build/test: Release build 0 lỗi; Safe tests 487/487 (cả trong audit của từng deploy).
5. Publish (source commit a917153, 2026-11-07 +07):
   - webhooks DEPLOYED 07:47: artifact 44802962f0b6f5620f77c6bfbceb01bc9ec1bfd35cfd5fe57f621b8b5859a06a, gói 09598F469F38BD5E251409C0ED09FAECCBA14396A5667AC689028BF0CE114DCA, backup C:\TADeploy\backups\webhooks-20261107-074720, health 200.
   - invoice DEPLOYED 07:49: artifact 8fd21df8eb306d30a844ce8833e090cf0ad34d63c1db9eb3bbf6a7d87064bdb5, gói 3824E74DD53277A53DD4D086748006337EDA47C8DCC12E36AB0C3B97532031EF, backup C:\TADeploy\backups\invoice-20261107-074922, health 200.
   - portal DEPLOYED 07:51: artifact 468ad34711da1573f993208056b9cbf640567902891e2bc26d5a75d83c1b1761, gói 63CF04989853F4B267B44A2C38F9A09D3B98F495B1DB9BC5C35F33E4621AD803, backup C:\TADeploy\backups\portal-20261107-075128, health 200.
6. Git: branch `feature/customer-portal-mvp01`, source commit a917153; main fast-forward.
7. Rủi ro/việc còn lại: cài đặt hiện có giữ IssueMode NULL (đọc như SIGNED nếu đang bật tự động) cho đến khi doanh nghiệp lưu lại lựa chọn.
8. Bằng chứng: SafeCustomerEInvoiceTests (2 test mới); log migration và log deploy.
