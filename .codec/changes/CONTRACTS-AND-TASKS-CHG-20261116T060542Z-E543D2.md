# Change CONTRACTS-AND-TASKS-CHG-20261116T060542Z-E543D2

- Feature ID: `CONTRACTS-AND-TASKS`
- Change ID: `CONTRACTS-AND-TASKS-CHG-20261116T060542Z-E543D2`
- Created UTC: 2026-11-16T06:05:42.092635+00:00
- Tiêu đề: Danh mục công việc: ô tìm có danh sách gợi ý tên đầu việc
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
- Yêu cầu: "Kiểm tra lại mất dropdownlist gợi ý khi gõ tên" ở trang Danh mục công việc.
- Ô "Tìm đầu việc" dùng combo gợi ý (data-ta-combo-free): gõ là lọc bảng và liệt kê tên đầu việc khớp (không phân biệt dấu); chọn một tên thì bảng lọc theo tên đó. Không chặn gửi form, không tự điền checklist.
- MODIFY src/TAPortal.Web/Views/WorkCatalog/Index.cshtml, src/TAPortal.Web/wwwroot/js/ta-work.js, src/TAPortal.Web/wwwroot/css/ta-admin.css, tests/TACustomerPortal.Tests/SafeWorkCatalogTests.cs
- Không có migration. Safe tests 782/782.
