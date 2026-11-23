# CODEC DELIVERY REPORT — TINGEE-INTEGRATION-CHG-20261123T090237Z-0F6D3B

Feature ID: `TINGEE-INTEGRATION`

1. Mục tiêu và kết quả: Viettel SInvoice bên cạnh MISA meInvoice trên trang Hóa đơn điện tử; thẻ "Sẵn sàng xuất tự động" và hướng dẫn các bước xuất ký số tự động. Chi tiết: `.codec/changes/TINGEE-INTEGRATION-CHG-20261123T090237Z-0F6D3B.md`.
2. File: xem change record.
3. Database `TAPortalSys`: 067-einvoice-viettel-provider.sql (verify 121, rollback 122) EXECUTED 2026-11-23T10:46:11Z; 066 ALREADY_APPLIED. Backup trước chạy: TAPortalSys-predeploy-20261123T104309Z.bak.
4. Build/test: Release build 0 lỗi; Safe 829/829; codec tests OK; codec validate chỉ cảnh báo đường dẫn publish của checkout; git diff --check OK.
5. Publish/deploy: portal DEPLOYED, invoice DEPLOYED, webhooks ROLLED_BACK (SQL login TA_User hết hạn mật khẩu; health 503 cả trước và sau rollback). Manifest: `.codec/reports/TINGEE-INTEGRATION-CHG-20261123T090237Z-0F6D3B-<PROJECT>.sha256`.
6. Git: branch feature/einvoice-multi-provider; source commit d779fd0; deploy commit 57bdaea; merge vào main (SHA ở hand-off).
7. Việc operator: (a) khôi phục login SQL `TA_User` (đổi mật khẩu + cập nhật connection string, hoặc tắt CHECK_EXPIRATION cho login dịch vụ) — webhooks, worker của portal và đăng nhập Customer Portal phụ thuộc vào nó; (b) sau đó deploy lại webhooks: `Deploy-TARemote.ps1 -Target webhooks -ChangeId TINGEE-INTEGRATION-CHG-20261123T090237Z-0F6D3B -MigrationsApplied`; (c) thử Viettel SInvoice thật qua Admin "Hóa đơn Sandbox".
8. Bằng chứng: log migration trong `.codec/reports/`, log deploy local `src/publish/deploy-logs/`, Event Log Application trên server (SqlException "password of the account has expired").
