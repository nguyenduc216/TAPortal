# CODEC DELIVERY REPORT — CONTRACTS-AND-TASKS-CHG-20261107T062339Z-50D5E9

Feature ID: `CONTRACTS-AND-TASKS`
Change ID: `CONTRACTS-AND-TASKS-CHG-20261107T062339Z-50D5E9`

1. Mục tiêu và kết quả thực tế: TA Portal có module Hợp đồng (theo khách hàng, file đính kèm) và Công việc: "Việc của tôi" là trang đích sau đăng nhập (việc chưa hoàn thành, sắp theo hạn / tương tác gần nhất, cập nhật nhanh trạng thái, checklist có thời điểm hoàn thành, trao đổi), danh sách việc theo quyền xem, giao việc nhiều người (một người chịu trách nhiệm chính), việc lặp theo kỳ, báo cáo tiến độ từng nhân viên, nút micro nhập giọng nói.
2. Danh sách file cập nhật: xem `.codec/changes/CONTRACTS-AND-TASKS-CHG-20261107T062339Z-50D5E9.md`.
3. Database: migration 031-contracts-and-work-tasks.sql (verify 051, rollback 052): pre-check NOT_APPLIED; dry-run PASS (rollback → NOT_APPLIED, @@TRANCOUNT=0); backup full 399 phút; gates 592/592; real run 2026-11-07T06:32:13Z VERIFY_STATUS=PASS ⇒ EXECUTED. Lần chạy đầu 06:24Z dừng ở gates (test SafeCustomerWorkspaceTests chập chờn khi chạy song song; chạy lại đạt), không có thay đổi nào được ghi.
4. Build/test: Release build 0 lỗi; Safe tests 592/592 (cả trong audit deploy).
5. Publish: portal DEPLOYED 13:36 (+07): source commit f665151, artifact f1a192cb2367435bbaf52ed1348a88a4606cf696ac2bedd312e5d9f9aadc543e, gói C880A0BC4CD2A41C6CD0370F4A31A31A90E2F10B049832A776773845394A2852, backup C:\TADeploy\backups\portal-20261107-133627, health 200. Smoke: /work, /contracts, /work/report chuyển về đăng nhập (302); Permissions-Policy microphone=(self); /js/ta-work.js 200. invoice và webhooks không đổi (không deploy).
6. Git: branch `feature/customer-portal-mvp01`, source commit f665151; main fast-forward.
7. Rủi ro/việc còn lại: chưa đăng nhập thử trên production; chỉ SYS_ADMIN có quyền mới — cần tạo vai trò nhân viên (TASKS.VIEW, TASKS.CREATE), quản lý (thêm TASKS.ASSIGN, TASKS.VIEW_ALL, TASKS.TEMPLATES.MANAGE, TASKS.REPORT.VIEW, CONTRACTS.*) và liên kết tài khoản với hồ sơ nhân viên; chưa có thông báo email/Zalo khi giao việc.
8. Bằng chứng: log migration `.codec/reports/CONTRACTS-AND-TASKS-CHG-20261107T062339Z-50D5E9-migrations-20261107T062954Z.log`; log deploy; SafeAdminWorkTests, SafeAdminContractTests.
