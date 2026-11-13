# CODEC DELIVERY REPORT — CONTRACTS-AND-TASKS-CHG-20261113T113022Z-FE45B3

Feature ID: `CONTRACTS-AND-TASKS`
Change ID: `CONTRACTS-AND-TASKS-CHG-20261113T113022Z-FE45B3`

1. Kết quả: sqlcmd đọc file SQL dạng UTF-8 (`-f i:65001`) trong Invoke-TAMigrations/Invoke-TAReadQuery; 72 giá trị chữ Việt bị lỗi đã sửa bằng migration dữ liệu một lần 040 (không lưu Git); Danh sách công việc gọn trên điện thoại.
2. File: xem `.codec/changes/CONTRACTS-AND-TASKS-CHG-20261113T113022Z-FE45B3.md`.
3. Database: 040 EXECUTED (dry-run PASS, gate 717/717, verify PASS).
4. Build/test: Release build 0 lỗi; Safe tests 717/717.
5. Publish: portal artifact c0b05232b40b, health 200. Source commit 6a3e43f. Deploy dùng -MigrationsApplied vì 017/033/037 bị gỡ khỏi Git (đã chạy từ trước).
6. Git: branch `feature/tx-sync`, source commit 6a3e43f; main fast-forward.
7. Rủi ro: chưa xem trên điện thoại thật.
8. Bằng chứng: log migration; log deploy; trang thử 375px/1280px.
