# CODEC DELIVERY REPORT — CUSTOMER-PORTAL-FOUNDATION-CHG-20261104T155401Z-13E2DF

Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261104T155401Z-13E2DF`

1. Mục tiêu và kết quả thực tế: sửa lỗi "Không đọc được dữ liệu gói dịch vụ" ở Admin "Giao dịch mua gói" và lịch sử mua gói Customer Portal (SqlException: column 'DisplayName' specified multiple times for 'x'). DEPLOYED.
2. Danh sách file cập nhật: xem `.codec/changes/CUSTOMER-PORTAL-FOUNDATION-CHG-20261104T155401Z-13E2DF.md`.
3. Database `TAPortalSys`: NOT_APPLICABLE.
4. Build/test: Safe tests 401/401 pass (test mới PurchaseSelectHasUniqueColumnNames).
5. Publish: DEPLOYED 2026-11-04 (+07), commit 5b473ea —
   - portal 22:57: checksum 1D491838…30FFE, backup C:\TADeployackups\portal-20261104-225711, health 200;
   - invoice 22:58: checksum E4A7B517…42857, backup C:\TADeployackups\invoice-20261104-225831, health 200.
   TAPortal.Shared.dll trên cả hai site chứa alias mới. Rollback: `Deploy-TARemote.ps1 -Target portal|invoice -Rollback`.
6. Git: branch `feature/customer-portal-mvp01`, source commit cdc3432; merge PENDING_APPROVAL.
7. Rủi ro/việc còn lại: chưa mở trang sau đăng nhập để xác nhận trực quan (operator kiểm tra).
8. Bằng chứng: log deploy, kiểm tra DLL trên server, log lỗi production trước khi sửa.
