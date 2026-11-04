# CODEC DELIVERY REPORT — TINGEE-INTEGRATION-CHG-20261104T154552Z-A2ACAA

Feature ID: `TINGEE-INTEGRATION`
Change ID: `TINGEE-INTEGRATION-CHG-20261104T154552Z-A2ACAA`

1. Mục tiêu và kết quả thực tế: Tingee Sandbox (Admin) mục 5 — thử Bank Link SDK (create-bank-link-session, bank-link/bank-config, popup/nhúng, liệt kê callback). DEPLOYED.
2. Danh sách file cập nhật: xem `.codec/changes/TINGEE-INTEGRATION-CHG-20261104T154552Z-A2ACAA.md`.
3. Database `TAPortalSys`: NOT_APPLICABLE.
4. Build/test: Safe tests 400/400 pass.
5. Publish: DEPLOYED — portal 6c3fea6 lên ta-server (TA.Portal) lúc 2026-11-04 22:48 (+07): checksum gói F51AD52A…070B3, backup C:\TADeploy\backups\portal-20261104-224829, health 200. Kiểm tra sau deploy: /Account/Login 200, /TingeeSandbox 302 (cần đăng nhập); TAPortal.Web.dll và TAPortal.Integrations.dll trên server có BankLinkSession, ta-tingee-sandbox.js có data-ta-banklink-form. Rollback: `Deploy-TARemote.ps1 -Target portal -Rollback`. Gói này gồm cả change TINGEE-INTEGRATION-CHG-20261104T124547Z-45D0B5 (e-invoice Sandbox).
6. Git: branch `feature/customer-portal-mvp01`, source commit 8729d4c, deploy commit 6c3fea6; merge PENDING_APPROVAL.
7. Rủi ro/việc còn lại: Tingee thật NOT_VERIFIED (client cần scope Bank). Màn hình SDK do Tingee hiển thị — chưa dùng cho khách hàng. Customer/Webhooks chưa republish (không đổi hành vi).
8. Bằng chứng: SafeAdminTingeeSandboxTests (2 test mới), log deploy, kiểm tra file trên server.
