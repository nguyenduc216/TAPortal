# CODEC DELIVERY REPORT — ADMIN-ROLE-PERMISSIONS-CHG-20261113T041647Z-E7EEDF
Feature ID: `ADMIN-ROLE-PERMISSIONS`
Change ID: `ADMIN-ROLE-PERMISSIONS-CHG-20261113T041647Z-E7EEDF`
1. Mục tiêu và kết quả thực tế: danh sách mật khẩu yếu và mật khẩu khởi tạo cài đặt trong portal; bắt buộc đổi mật khẩu yếu ở cả portal và hóa đơn; mật khẩu mới gửi về email; xóa mềm tài khoản/việc thử.
2. Danh sách file cập nhật: xem `.codec/changes/ADMIN-ROLE-PERMISSIONS-CHG-20261113T041647Z-E7EEDF.md`.
3. Database: migration 037 EXECUTED (verify 063 PASS); rollback 064.
4. Build/test: Release build 0 lỗi; Safe tests 677/677.
5. Publish: webhooks 34550358…, portal dd32ee88…, invoice c9ea1408…; health 200. Source commit 3fc1dab.
6. Git: branch `feature/tx-sync`, source commit 3fc1dab; main fast-forward.
7. Rủi ro/việc còn lại: mật khẩu mới gửi qua email dạng chữ thường (theo yêu cầu operator).
8. Bằng chứng: Safe tests; log migration; log deploy; kiểm tra đếm bằng Invoke-TAReadQuery.
