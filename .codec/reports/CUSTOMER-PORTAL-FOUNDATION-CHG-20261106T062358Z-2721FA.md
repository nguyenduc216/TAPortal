# CODEC DELIVERY REPORT — CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T062358Z-2721FA

Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T062358Z-2721FA`

1. Mục tiêu và kết quả: logo trong danh sách Khách hàng và thanh bên Customer Portal; nhân viên T.A xem/tải lên/xoá logo doanh nghiệp trong
   Admin (Customer Workspace → Hồ sơ doanh nghiệp, header mọi tab). DEPLOYED.
2. File: xem `.codec/changes/CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T062358Z-2721FA.md`.
3. Database: không có migration mới (dùng 024, EXECUTED).
4. Build/test: Release build 0 errors; Safe 478/478 PASS trên commit sạch bcec7af; codec unittest OK; git diff --check OK.
5. Deploy: invoice, portal, webhooks DEPLOYED (source bcec7af), health 200; SHA256 trong change record.
6. Git: feature/customer-portal-mvp01, source bcec7af; merge main sau commit ghi nhận này.
7. Rủi ro/rollback: `Apply-TARelease.ps1 -Rollback`; logo cache 1 phút giữa 2 site.
8. Thay đổi chưa commit của session khác trong checkout chính không được commit hay deploy trong change này.
