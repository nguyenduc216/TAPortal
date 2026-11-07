# CODEC DELIVERY REPORT — CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T235029Z-86E87C

Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T235029Z-86E87C`

1. Mục tiêu và kết quả thực tế: khi MISA không ký được hóa đơn (400 CallSignServiceFail) thì tự tạo hóa đơn nháp cùng mã hóa đơn của đơn, đơn chuyển "Đã xuất nháp" (DRAFT) thay vì lỗi; xuất lại được khi chữ ký số dùng được.
2. Danh sách file cập nhật: xem `.codec/changes/CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T235029Z-86E87C.md`.
3. Database: migration 026-order-invoice-draft.sql (verify 041, rollback 042).
   - Pre-check 2026-11-06T23:51:23Z: VERIFY_STATUS=NOT_APPLIED.
   - Dry-run (migration → verify PASS → rollback → verify NOT_APPLIED → ROLLBACK, @@TRANCOUNT=0) 23:51:24Z: PASS; pre-check lại NOT_APPLIED.
   - Backup COPY_ONLY + RESTORE VERIFYONLY: TAPortalSys-predeploy-20261106T235121Z.bak (bản full gần nhất cũ 1956 phút).
   - Gates restore/build/Safe tests 485/485: PASS.
   - Real run 23:53:11Z: VERIFY_STATUS=PASS ⇒ EXECUTED.
4. Build/test: Release build 0 lỗi; Safe tests 485/485 (cả trong audit của từng deploy).
5. Publish (source commit 4c6618d, 2026-11-07 +07):
   - webhooks DEPLOYED 06:56: artifact 09fe56a172ad16d6c765050019d7a0628d2c3dbbb26256a7809c93da1f401161 (52 file), gói 076E34A77E548EE33646BB02BF4326BB34BD3015569F6B181614FC286ECBD204, backup C:\TADeploy\backups\webhooks-20261107-065559, health 200.
   - invoice DEPLOYED 06:58: artifact 3088b32eb315fb36aca49aa8cf4413d08b4d096183aee1c176f51dfd9e4e74b7 (72 file), gói 7073CB3FEC48AE4EEF99BA5E2F5D0E85E89D948CB0339FF8AC23ACCC050B56FF, backup C:\TADeploy\backups\invoice-20261107-065754, health 200.
   - portal DEPLOYED 06:59: artifact ef2d7b1fd083f7a7b7a422acb86c366bd4b3284fd65ace9001b39df602516c31 (65 file), gói E7F00E384FEDF1AA7D789006FE808E839B3E029DED5E78B0140D602AD2318CA4, backup C:\TADeploy\backups\portal-20261107-065950, health 200.
6. Git: branch `feature/customer-portal-mvp01`, source commit 4c6618d; main fast-forward.
7. Rủi ro/việc còn lại: chưa thử với MISA thật (không tự xuất thay operator); đơn DH261106-0001 vẫn FAILED đến khi operator bấm "Xuất lại hóa đơn". Hóa đơn nháp được phát hành trên MISA thì T.A chưa tự cập nhật.
8. Bằng chứng: SafeCustomerEInvoiceTests (4 test mới); log migration và log deploy.
