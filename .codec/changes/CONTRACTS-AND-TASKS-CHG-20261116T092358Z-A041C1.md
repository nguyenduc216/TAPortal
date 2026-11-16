# Change CONTRACTS-AND-TASKS-CHG-20261116T092358Z-A041C1

- Feature ID: `CONTRACTS-AND-TASKS`
- Change ID: `CONTRACTS-AND-TASKS-CHG-20261116T092358Z-A041C1`
- Created UTC: 2026-11-16T09:23:58.929943+00:00
- Tiêu đề: Checklist mẫu: lưu là thêm mục còn thiếu vào các việc cùng tên đang làm
- Trạng thái: PLANNED

## Yêu cầu và phạm vi

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path:
- Thứ tự chạy:
- Preflight / Verify / Rollback:
- Execution: NOT_EXECUTED

## Test và kết quả

## Publish
- Project:
- Destination (chỉ từ config):
- Trạng thái: NOT_PUBLISHED
- Artifact SHA256:
- Smoke test:

## Git
- Branch:
- Commit SHA:
- PR:
- Merge: PENDING_APPROVAL
- Merge SHA:

## Rủi ro, quyết định và việc còn lại

## Ghi chú thực hiện
- Yêu cầu: các mục checklist mẫu ("ád", "123123") phải lưu được và hiện trong công việc giống mục "Soạn PLHĐ…"; popup "Sửa đầu việc" phải rõ đang sửa đầu việc nào ("thêm mới thì sẽ là thêm cho công việc này").
- Lưu đầu việc (popup hoặc sửa nhanh): mỗi mục của checklist mẫu còn thiếu được thêm vào cuối checklist của các công việc cùng tên (tên đúng hoặc "tên — chi tiết") đang Mới / Đang làm; việc Hoàn thành / Đã hủy giữ nguyên; không thêm trùng; tối đa ChecklistItemsMax mục; ghi lịch sử "Thêm từ checklist mẫu: …" trên từng việc và tính lại Progress. Thông báo cho biết số mục đã thêm; audit có addedToTasks.
- Popup ghi "Sửa đầu việc: <tên>", giải thích phạm vi áp dụng trong tab Checklist.
- SQL WorkCatalogSql.ApplyToTasks đã chạy thử trên LocalDB (DB tạm, xóa sau khi thử): đúng việc đang làm, bỏ qua việc DONE và việc khác tên, không trùng.
- MODIFY Services/WorkCatalogStore.cs, Controllers/WorkCatalogController.cs, Views/WorkCatalog/_CatalogDialog.cshtml, wwwroot/css/ta-admin.css, tests SafeWorkCatalogTests.cs. Không có migration. Safe tests 784/784.
