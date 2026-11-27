# CODEC DELIVERY REPORT — TINGEE-INTEGRATION-CHG-20261127T130319Z-E75A07

Feature ID: `TINGEE-INTEGRATION`
Change ID: `TINGEE-INTEGRATION-CHG-20261127T130319Z-E75A07`

1. Mục tiêu và kết quả: "Thu gộp công nợ". Gộp nhiều đơn còn nợ của một khách vào một mã QR. Khách trả hết hoặc trả trước một phần; tiền chia vào các đơn theo thứ tự (đơn cũ trước). Đơn đủ tiền tự xuất hóa đơn như cũ.
2. File: xem change.
3. Database `TAPortalSys`: migration 069 EXECUTED 2026-11-27 13:24:40Z (dry-run có rollback PASS, backup COPY_ONLY đã kiểm tra, verify PASS).
4. Build/test: Release 0 lỗi; Safe tests 867/867 PASS.
5. Publish: portal, invoice, webhooks DEPLOYED từ 0f21276, health HTTP 200 (artifact/package SHA256 trong change).
6. Git: branch feature/payment-collections; merge main sau deploy.
7. Rollback: `Deploy-TARemote.ps1 -Target portal,invoice,webhooks -Rollback`; DB `126-rollback-payment-collections.sql` (từ chối khi đã có phiếu thu).
8. Bằng chứng: log migration trong `.codec/reports`, log deploy `src/publish/deploy-logs/deploy-20261127T132501Z.log`, smoke test 404/302.
