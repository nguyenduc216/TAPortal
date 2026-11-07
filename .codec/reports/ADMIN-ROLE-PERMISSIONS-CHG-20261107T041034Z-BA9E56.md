# CODEC DELIVERY REPORT — ADMIN-ROLE-PERMISSIONS-CHG-20261107T041034Z-BA9E56

Feature ID: `ADMIN-ROLE-PERMISSIONS`
Change ID: `ADMIN-ROLE-PERMISSIONS-CHG-20261107T041034Z-BA9E56`

1. Mục tiêu và kết quả thực tế: chỉ SYS_ADMIN tạo/sửa/xóa vai trò và gán vai trò cho tài khoản (kể cả khi tạo tài khoản).
2. Danh sách file cập nhật: xem `.codec/changes/ADMIN-ROLE-PERMISSIONS-CHG-20261107T041034Z-BA9E56.md`.
3. Database: không có migration.
4. Build/test: Release build 0 lỗi; Safe tests 562/562 (cả trong audit của từng deploy).
5. Publish (source commit 9d2764d, 2026-11-07 +07; manifest `.codec/reports/ADMIN-ROLE-PERMISSIONS-CHG-20261107T041034Z-BA9E56-*.sha256`):
   - portal DEPLOYED 11:16: artifact 6bf4fa938442553033e20940b4b703cab278cef70690137317f3c7625c2fe1ef, gói 3F4F24185461FB1A4EDF86D0EBEBED6CAAE917D780A984025CA0348728A1C0A5, backup C:\TADeploy\backups\portal-20261107-111629, health 200.
   - invoice DEPLOYED 11:18: artifact 441868eef956fad9bdb0d148a4447eb1daddd367231cdbe12955fcc8d93d73bb, gói 6BEA8C3DE536114255C08B61D689ECC9EAED58A17D3E8D11BC53C57C0FA8FF9D, backup C:\TADeploy\backups\invoice-20261107-111848, health 200.
   - webhooks DEPLOYED 11:21: artifact a18173563fb3cbae40cb01b41a98d930367d67caf1686e30b35dc02bbd0dcf54, gói D761DB1B0763623E2279FCDA5BBC8A9C7866174BAF0D3AF6728247703B68FD93, backup C:\TADeploy\backups\webhooks-20261107-112106, health 200.
6. Git: branch `feature/customer-portal-mvp01`, source commit 9d2764d; main fast-forward.
7. Rủi ro/việc còn lại: chưa đăng nhập thử trên production.
8. Bằng chứng: log migration/deploy; SafeAdminPermissionTests, SafeAdminEmployeeTests.
