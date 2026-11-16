# Change CONTRACTS-AND-TASKS-CHG-20261114T021052Z-F6F9C9

- Feature ID: `CONTRACTS-AND-TASKS`
- Change ID: `CONTRACTS-AND-TASKS-CHG-20261114T021052Z-F6F9C9`
- Created UTC: 2026-11-14T02:10:52.069333+00:00
- Tiêu đề: Danh muc cong viec: chon ten viec tu danh muc, goi y khi go, khong nhap tu do
- Trạng thái: DEPLOYED

## Yêu cầu và phạm vi
Operator (2026-11-14): đầu việc giống nhau (ví dụ "Kéo hóa đơn") phải thành danh mục công việc; người dùng gõ chữ thì gợi ý, không cho nhập linh tinh. Operator chọn: chỉ người quản lý thêm danh mục; gom tên các việc cũ về danh mục.

Đã làm:
- Migration 043 `dbo.WorkTaskCatalog` (tên duy nhất, mô tả, thứ tự, đang dùng) + quyền `TASKS.CATALOG.MANAGE` (SYS_ADMIN).
- Công việc > Danh mục công việc (`WorkCatalogController`): thêm, sửa tên/mô tả/thứ tự, ẩn, xóa mềm; đổi tên thì các việc và việc lặp mang tên cũ đổi theo; audit.
- Tạo/sửa việc và việc lặp: ô "Tên công việc" thành combobox gợi ý (gõ không dấu, nhiều từ, ↑/↓/Enter, click); không gửi được tên ngoài danh mục; server kiểm tra lại (`WorkCatalogSql.Resolve`) và lưu đúng chính tả danh mục. Việc cũ giữ tên nếu không đổi. Danh mục trống thì nhập tự do như trước.
- Dữ liệu danh mục ban đầu, gom tên việc cũ và nhập việc của Hà: migration dữ liệu một lần 045 (không lưu Git, theo quy tắc AGENTS.md).

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `database/migrations/043-work-task-catalog.sql`, `database/scripts/075-verify-work-task-catalog.sql`, `database/scripts/076-rollback-work-task-catalog.sql`
- ADD `src/TAPortal.Web/Services/WorkCatalogStore.cs`, `src/TAPortal.Web/Controllers/WorkCatalogController.cs`, `src/TAPortal.Web/Views/WorkCatalog/Index.cshtml`, `src/TAPortal.Web/Views/Work/_CatalogTitle.cshtml`, `tests/TACustomerPortal.Tests/SafeWorkCatalogTests.cs`
- MODIFY `Services/WorkTaskStore.cs`, `Models/WorkModels.cs`, `Security/AdminPermissions.cs`, `Program.cs`, `Views/Shared/_Layout.cshtml`, `Views/Work/Edit.cshtml`, `Views/Work/TemplateEdit.cshtml`, `wwwroot/css/ta-admin.css`, `wwwroot/js/ta-work.js`, `tests/TACustomerPortal.Tests/SafeAdminWorkTests.cs`

## Migration SQL
- Database: TAPortalSys
- Script: `database/migrations/043-work-task-catalog.sql` (sau 042)
- Verify 075, Rollback 076.
- Execution: EXECUTED (Invoke-TAMigrations 043:075:076).
- 045: migration dữ liệu một lần (danh mục ban đầu, gom tên việc cũ, khách mới, việc của Hà ngày 14/11), chạy rồi xóa.

## Test và kết quả
- Safe tests 743/743 (thêm SafeWorkCatalogTests 5, TheTaskNameIsPickedFromTheCatalogueWhenOneExists).
- Chưa thử gõ combobox trong trình duyệt thật.

## Publish
- portal. Xem report.

## Git
- Branch: feature/tx-sync
- Commit SHA: f3932f4
- Merge: main

## Rủi ro, quyết định và việc còn lại
- Chưa thử combobox trên trình duyệt/điện thoại thật.
