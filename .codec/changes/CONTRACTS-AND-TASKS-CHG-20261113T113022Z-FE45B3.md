# Change CONTRACTS-AND-TASKS-CHG-20261113T113022Z-FE45B3

- Feature ID: `CONTRACTS-AND-TASKS`
- Change ID: `CONTRACTS-AND-TASKS-CHG-20261113T113022Z-FE45B3`
- Created UTC: 2026-11-13T11:30:22.553137+00:00
- Tiêu đề: Repair garbled Vietnamese (sqlcmd UTF-8 input), compact task list on mobile
- Trạng thái: DEPLOYED

## Yêu cầu và phạm vi
Operator (2026-11-13, ảnh Danh sách công việc trên điện thoại): làm gọn giao diện; sửa lỗi tiếng Việt do migration (chạy lại/update tùy).

1. Nguyên nhân lỗi chữ: `Invoke-TAMigrations.ps1` và `Invoke-TAReadQuery.ps1` tải file SQL UTF-8 (không BOM) lên server, `sqlcmd` đọc theo bảng mã ANSI => chữ có dấu bị lưu sai (UTF-8 đọc như Windows-1252). Đã thêm `-f i:65001` cho cả hai; thử đọc "Thủy Đức" => UTF8_OK.
2. Sửa dữ liệu: quét mọi cột nvarchar; 72 giá trị bị lỗi được sửa bằng migration dữ liệu một lần 040 (verify 069, rollback 070; file chỉ ASCII dùng NCHAR): Employees.FullName 6, Users.DisplayName 6, EmployeeStatusHistories.Reason 6, WorkTasks.Title 18, WorkTasks.Description 18, WorkTaskActivities.Body 18. Dry-run PASS, gate 717/717, real run EXECUTED, verify PASS. Theo quy tắc, file 040/069/070 đã xóa, không commit. Các cột còn lại bị bộ quét bắt là chữ Việt đúng (không đổi được).
3. Giao diện Danh sách công việc (≤767px): ô tìm kiếm một dòng, các bộ lọc thành một hàng trượt ngang (cao 36px); mỗi công việc là thẻ gọn (tên + trạng thái, phụ trách, khách hàng, hạn + tiến độ). Desktop giữ bảng.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `deploy/windows/Invoke-TAMigrations.ps1`, `deploy/windows/Invoke-TAReadQuery.ps1`
- MODIFY `src/TAPortal.Web/Views/Work/Tasks.cshtml`, `src/TAPortal.Web/wwwroot/css/ta-admin.css`

## Migration SQL
- 040 (một lần, không lưu Git): EXECUTED 2026-11-13 11:33Z.

## Test và kết quả
- Trang thử 375px: khung lọc 117px (trước ~560px), mỗi thẻ ~140px (trước ~470px), không tràn ngang; 1280px giữ bảng.

## Publish
- portal. Xem report.

## Git
- Branch: feature/tx-sync
- Commit SHA: 6a3e43f
- Merge: fast-forward main

## Rủi ro, quyết định và việc còn lại
- Chưa xem trên điện thoại thật.
