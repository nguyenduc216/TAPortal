# CODEC DELIVERY REPORT — ADMIN-ROLE-PERMISSIONS-CHG-20261107T020425Z-9C2068

Feature ID: `ADMIN-ROLE-PERMISSIONS`
Change ID: `ADMIN-ROLE-PERMISSIONS-CHG-20261107T020425Z-9C2068`

1. Mục tiêu và kết quả thực tế: trang quản trị có quản lý vai trò + tích quyền theo chức năng; mọi trang/hành động kiểm tra theo quyền thay vì chỉ SYS_ADMIN.
2. Danh sách file cập nhật: xem `.codec/changes/ADMIN-ROLE-PERMISSIONS-CHG-20261107T020425Z-9C2068.md`.
3. Database: migration 028-admin-permission-catalogue.sql (verify 045, rollback 046): pre-check NOT_APPLIED 02:05:27Z; dry-run (migration → verify PASS → rollback → verify NOT_APPLIED → ROLLBACK, @@TRANCOUNT=0) PASS; backup full 134 phút (trong 24 giờ); gates 553/553 PASS; real run 02:07:12Z VERIFY_STATUS=PASS ⇒ EXECUTED.
4. Build/test: Release build 0 lỗi; Safe tests 553/553 (sau khi gộp hai phần; cả trong audit của từng deploy).
5. Publish (source commit c16e082, 2026-11-07 +07; manifest `.codec/reports/ADMIN-ROLE-PERMISSIONS-CHG-20261107T020425Z-9C2068-*.sha256`):
   - portal DEPLOYED 09:10: artifact d6d42fcf52d03aab6cc31942748e7fadbb5e12dc021203f6c40dd69b88bf452c, gói 0EC647C653D43C80B297F66CD7C41A03EB1345B4F604B4F26FD4492A7FE8199E, backup C:\TADeploy\backups\portal-20261107-091015, health 200.
   - invoice DEPLOYED 09:12: artifact 0cba1a988b04e9d9f51c7e9a5e2b88bcf490aff88bb25374490d1c5186d5d85c, gói FE5496D43750EFBD4BF37FB04BD5CCBE21A7042373515DB745AEA50334D7D5CA, backup C:\TADeploy\backups\invoice-20261107-091215, health 200.
   - webhooks DEPLOYED 09:14: artifact a4e5c3f8d86246430a7fcde2f3d35ba9317316f4c279fc0642bcb0f0f98a8f2c, gói 142543A92137981DF2810FC22B988DEE14288265812D51F5310337A2EB169828, backup C:\TADeploy\backups\webhooks-20261107-091429, health 200.
   - Kiểm tra sau deploy: /members (invoice) và /Roles (portal) chuyển về trang đăng nhập khi chưa đăng nhập (302); chưa đăng nhập thử bằng tài khoản thật.
6. Git: branch `feature/customer-portal-mvp01`; nhánh agent gộp bằng merge commit 3daf4d3, source commit c16e082; main fast-forward.
7. Rủi ro/việc còn lại: xem mục rủi ro trong change.
8. Bằng chứng: SafeAdminPermissionTests và các test Safe Admin đã cập nhật; log migration và log deploy.
