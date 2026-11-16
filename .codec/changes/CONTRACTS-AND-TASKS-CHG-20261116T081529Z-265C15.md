# Change CONTRACTS-AND-TASKS-CHG-20261116T081529Z-265C15

- Feature ID: `CONTRACTS-AND-TASKS`
- Change ID: `CONTRACTS-AND-TASKS-CHG-20261116T081529Z-265C15`
- Created UTC: 2026-11-16T08:15:29.204742+00:00
- Tiêu đề: Công việc: kéo thả đổi thứ tự checklist trong từng công việc
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
- Yêu cầu: cho kéo thả sắp xếp lại checklist trong từng công việc (operator "Có, làm luôn").
- Trang chi tiết công việc và thẻ việc (Việc của tôi): mỗi mục có tay kéo (khi việc chưa hủy và có từ 2 mục); kéo bằng chuột/cảm ứng hoặc chọn tay kéo rồi ↑/↓. Thứ tự mới lưu ngầm (fetch) vào WorkTaskChecklistItems.SortOrder, trang không tải lại; lỗi thì báo và tải lại trang.
- Server: WorkController.ChecklistOrder(id, items[]) -> IWorkTaskStore.OrderChecklistAsync (kiểm quyền xem việc, danh sách không trùng, tối đa ChecklistItemsMax; ghi lịch sử "Đổi thứ tự checklist" khi có thay đổi).
- MODIFY Services/WorkTaskStore.cs, Controllers/WorkController.cs, Views/Work/Details.cshtml, Views/Work/_TaskCard.cshtml, wwwroot/js/ta-work.js, wwwroot/css/ta-admin.css, tests SafeAdminWorkTests.cs.
- Không có migration (cột SortOrder có từ migration 031). Safe tests 784/784.
