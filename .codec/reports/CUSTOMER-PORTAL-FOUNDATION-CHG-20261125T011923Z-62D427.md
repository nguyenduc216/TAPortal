# CODEC DELIVERY REPORT — CUSTOMER-PORTAL-FOUNDATION-CHG-20261125T011923Z-62D427

Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261125T011923Z-62D427`

1. Mục tiêu và kết quả thực tế: trang `/Notifications/ZaloGuide` hướng dẫn doanh nghiệp tự kết nối Zalo OA (7 bước + lỗi thường gặp), link trong thẻ Zalo OA. DEPLOYED invoice.
2. Danh sách file cập nhật (relative + nếu cần absolute): xem change record (5 file source/test + Codec).
3. Database `TAPortalSys`: NOT_APPLICABLE (không migration).
4. Build/test: restore OK; Release build 0 lỗi; Safe tests 830/830 (audit deploy, nền main); codec unittest OK; codec validate chỉ báo lệch publish path do checkout khác workspace operator (WARN có sẵn).
5. Publish: TACustomerPortal.Web → invoice; artifact 67f4a3865cf6193e50f2dd67453b58f2cc217d26d0cd5c50b93f483ec2dee891; package 9EAA808C7929F0CE823301235342C4DFEF32A9BD9FA4F371879671E56697888F; DEPLOYED, health 200.
6. Git: feature/zalo-oa-guide, source c126e55, merge vào main fast-forward (delivery SHA báo trong hand-off).
7. Rủi ro/rollback/việc operator cần làm: rollback `Deploy-TARemote.ps1 -Target invoice -Rollback`. feature/customer-portal-mvp01 đang chậm hơn main — phiên sau làm trên mvp01 cần cập nhật từ main trước khi deploy. Hướng dẫn trên website công khai: chưa làm.
8. Bằng chứng kiểm tra (URL/log, không có secrets): server log apply-invoice 2026-11-26 12:31 RESULT: DEPLOYED c126e55; C:\TADeploy\state\invoice.txt = c126e55.
