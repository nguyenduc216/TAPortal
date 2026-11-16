# Change CONTRACTS-AND-TASKS-CHG-20261116T062438Z-9435EF

- Feature ID: `CONTRACTS-AND-TASKS`
- Change ID: `CONTRACTS-AND-TASKS-CHG-20261116T062438Z-9435EF`
- Created UTC: 2026-11-16T06:24:38.670342+00:00
- Tiêu đề: Danh mục công việc: nút Hủy hiện khi đang sửa dòng/checklist mẫu
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
- Yêu cầu: "chưa thấy icon cancel cho tác vụ edit / thêm mới" (Danh mục công việc).
- Lỗi: bấm một mục gợi ý (chip) để thêm vào checklist mẫu không đánh dấu dòng "có thay đổi" vì chip bị gỡ khỏi trang trước khi xác định dòng -> nút Hủy ở cột Thao tác không hiện.
- Sửa: listener đánh dấu chạy ở capture phase và đọc dòng ngay; nút Hủy ở cột Thao tác hiện khi dòng có thay đổi HOẶC đang mở checklist mẫu.
- MODIFY src/TAPortal.Web/wwwroot/js/ta-work.js, tests/TACustomerPortal.Tests/SafeWorkCatalogTests.cs. Không có migration.
