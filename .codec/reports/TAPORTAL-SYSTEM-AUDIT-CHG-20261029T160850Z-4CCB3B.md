# CODEC DELIVERY REPORT — TAPORTAL-SYSTEM-AUDIT-CHG-20261029T160850Z-4CCB3B

Feature ID: `TAPORTAL-SYSTEM-AUDIT`
Change ID: `TAPORTAL-SYSTEM-AUDIT-CHG-20261029T160850Z-4CCB3B`
Agent: Claude Code (claude-opus-5-5)

1. Mục tiêu và kết quả thực tế: Customer Workspace (Tổng quan / Thông tin khách hàng / Tài khoản Portal) với quản lý membership theo đúng khách hàng; bỏ menu sidebar "Tài khoản Customer Portal". IMPLEMENTED, BUILD_VERIFIED, LOCAL_PUBLISH_COMPLETE (Admin); VISUAL_NOT_VERIFIED trên môi trường thật; PRODUCTION_NOT_DEPLOYED.
2. Danh sách file cập nhật: 16 file trong source commit (xem change record); Shared và Customer app không đổi.
3. Database `TAPortalSys`: không migration, không SQL (NOT_APPLICABLE).
4. Build/test: restore OK; Release build 0/0; safe tests 250/250 PASS; Codec unittest OK, validate PASS; git diff --check OK.
5. Publish:

| Project | Destination | Exit | Files | Artifact SHA256 |
|---|---|---|---|---|
| TAPortal.Web | D:\T.A\TAPortal\TAPortal\publish\deployed-TAPortal.Web | 0 | 62 | f609696d3ead90bca8b464902bcde63277a70cfcf06b409aa44dba8df710e607 |
| TACustomerPortal.Web | — | NOT_REQUIRED | 58 | b39bb31c86eeb817b200d34e43df0c85a350a069aac051f937d7b9aa6477dc0e (giữ nguyên) |

   Source SHA eb8d0e656654a16c8f78930977bc8ad63e6f4a53. Backup publish\rollback\TAPORTAL-SYSTEM-AUDIT-CHG-20261029T160850Z-4CCB3B\TAPortal.Web.
6. Git: feature/customer-portal-mvp01; delivery SHA báo sau push; PR #7 draft, merge PENDING_APPROVAL.
7. Rủi ro/rollback/việc operator: UAT workspace trên Production (gán/tạo/thu hồi trên khách hàng test); quyết định trang tổng hợp cũ, kiểm tra trạng thái khách hàng khi kích hoạt lại ở backend, lọc danh sách tài khoản có sẵn. Rollback bằng thư mục backup.
8. Bằng chứng: manifest .codec/reports/TAPORTAL-SYSTEM-AUDIT-CHG-20261029T160850Z-4CCB3B-TAPortal.Web.sha256 (sha256sum -c PASS); SafeCustomerWorkspaceTests (cô lập customer, 404/403, CSRF, redirect tương thích).
