# CODEC DELIVERY REPORT — CUSTOMER-PORTAL-FOUNDATION-CHG-20261107T071821Z-4EA212

Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261107T071821Z-4EA212`

1. Mục tiêu và kết quả thực tế: kênh SMTP hệ thống gửi với tên "Hóa đơn tự động"; cấu hình site TA.Invoice không có FromName nên email đặt lại mật khẩu dùng mặc định "Hóa đơn tự động" — không sửa cấu hình server.
2. Danh sách file cập nhật: xem `.codec/changes/CUSTOMER-PORTAL-FOUNDATION-CHG-20261107T071821Z-4EA212.md`.
3. Database: migration 033 (verify 055, rollback 056): pre-check NOT_APPLIED; dry-run PASS (rollback → NOT_APPLIED, @@TRANCOUNT=0); gates 594/594; real run 2026-11-07T07:21:34Z VERIFY_STATUS=PASS ⇒ EXECUTED. Log `.codec/reports/CUSTOMER-PORTAL-FOUNDATION-CHG-20261107T071821Z-4EA212-migrations-20261107T071848Z.log`.
4. Build/test: gates PASS.
5. Publish: NOT_APPLICABLE.
6. Git: branch `feature/customer-portal-mvp01`, commit 0a60596; main fast-forward.
7. Rủi ro/việc còn lại: kênh SMTP hệ thống đang tắt.
8. Bằng chứng: log migration.
