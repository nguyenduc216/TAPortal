# CODEC DELIVERY REPORT — ADMIN-ROLE-PERMISSIONS-CHG-20261107T030206Z-6AC11F

Feature ID: `ADMIN-ROLE-PERMISSIONS`
Change ID: `ADMIN-ROLE-PERMISSIONS-CHG-20261107T030206Z-6AC11F`

1. Mục tiêu và kết quả thực tế: T.A Portal có quản lý nhân viên (hồ sơ, phân công, trạng thái, thông tin riêng tư theo quyền), tài khoản đăng nhập gắn với nhân viên, danh mục phòng ban/nhóm/chức danh, quyền Nhân sự trên trang Vai trò.
2. Danh sách file cập nhật: xem `.codec/changes/ADMIN-ROLE-PERMISSIONS-CHG-20261107T030206Z-6AC11F.md`.
3. Database: không có migration.
4. Build/test: Release build 0 lỗi; Safe tests 562/562 (cả trong audit deploy).
5. Publish: portal DEPLOYED 2026-11-07 10:05 (+07) từ commit f5bcc82: artifact 9b0b469a03fe55da3cfafd890ad33b987e1a9d96bc1ebc8f8f17b7b466f2335d (65 file), gói 49985FDE40D2CFC6002F1FC7BED005499206DDDB580E690B8C309B6A2DA5A12D, backup C:\TADeploy\backups\portal-20261107-100544, health 200; /Employees chuyển về đăng nhập khi chưa đăng nhập. Invoice/webhooks không đổi.
6. Git: branch `feature/customer-portal-mvp01`, source f5bcc82; main fast-forward.
7. Rủi ro/việc còn lại: xem change (SQL ghi chưa chạy với DB thật ngoài test fake).
8. Bằng chứng: SafeAdminEmployeeTests; log deploy.
