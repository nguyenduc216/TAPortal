# Change CONTRACTS-AND-TASKS-CHG-20261116T094307Z-E76F9F

- Feature ID: `CONTRACTS-AND-TASKS`
- Change ID: `CONTRACTS-AND-TASKS-CHG-20261116T094307Z-E76F9F`
- Created UTC: 2026-11-16T09:43:07.124420+00:00
- Tiêu đề: Lọc công việc theo Ngày tạo; danh sách chọn trong popup; trang chi tiết trải rộng
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
- Yêu cầu: "Phần khoảng thời gian không có lựa chọn khác, thêm ngày tạo, ngày bắt đầu, và mục chi tiết công việc chưa được trải rộng ra".
- Nguyên nhân không thấy lựa chọn: ô chọn tự vẽ (ta-select, ta-portal.js) gắn danh sách vào <body>, nằm dưới popup <dialog> modal nên bị che. Nay gắn vào dialog đang chứa ô chọn (cũng sửa cho mọi ô chọn khác trong popup).
- Bộ lọc Khoảng thời gian: Hạn hoàn thành, Ngày bắt đầu (đã có), thêm Ngày tạo (CreatedAt giờ VN), Ngày hoàn thành.
- Trang chi tiết công việc: Thông tin và Checklist chia hết chiều ngang (3:2), Trao đổi và lịch sử bên dưới; dưới 992px xếp một cột.
- MODIFY wwwroot/js/ta-portal.js, Services/WorkTaskStore.cs, Views/Work/Details.cshtml, wwwroot/css/ta-admin.css, tests SafeTaskFilterTests.cs. Không có migration. Safe tests 784/784.
