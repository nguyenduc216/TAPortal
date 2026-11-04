# CODEC DELIVERY REPORT — TINGEE-INTEGRATION-CHG-20261104T163209Z-755A18

Feature ID: `TINGEE-INTEGRATION`
Change ID: `TINGEE-INTEGRATION-CHG-20261104T163209Z-755A18`

1. Mục tiêu và kết quả thực tế: popup liên kết/xóa liên kết ngân hàng trong Tingee Sandbox cập nhật tại chỗ (không tải lại trang, hết nháy); ô chọn merchant con của Tingee Sandbox và Hóa đơn Sandbox đi qua soft navigation. DEPLOYED.
2. Danh sách file cập nhật: xem `.codec/changes/TINGEE-INTEGRATION-CHG-20261104T163209Z-755A18.md`.
3. Database `TAPortalSys`: NOT_APPLICABLE.
4. Build/test: Safe tests 402/402 pass; `node --check` OK.
5. Publish: DEPLOYED portal 84e166a lúc 2026-11-04 23:34 (+07): checksum D33BFF2F…17C4DA, backup C:\TADeploy\backups\portal-20261104-233417, health 200; /js/ta-tingee-sandbox.js trên production có handler mới; /Account/Login 200. Rollback: `Deploy-TARemote.ps1 -Target portal -Rollback`.
6. Git: branch `feature/customer-portal-mvp01`, source commit 123371c; merge PENDING_APPROVAL.
7. Rủi ro/việc còn lại: trình duyệt thật NOT_VERIFIED — operator thử đổi ngân hàng/loại tài khoản trong popup.
8. Bằng chứng: SafeAdminTingeeSandboxTests.LinkPopupPickersAreHandledInPlace, log deploy, file JS trên production.
