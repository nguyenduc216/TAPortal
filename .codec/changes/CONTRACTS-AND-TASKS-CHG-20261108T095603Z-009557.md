# Change CONTRACTS-AND-TASKS-CHG-20261108T095603Z-009557

- Feature ID: `CONTRACTS-AND-TASKS`
- Change ID: `CONTRACTS-AND-TASKS-CHG-20261108T095603Z-009557`
- Created UTC: 2026-11-08T09:56:03.054501+00:00
- Tiêu đề: Bo chon nhan vien co avatar, tick chon (giao viec)
- Trạng thái: DEPLOYED

## Yêu cầu và phạm vi
Operator (2026-11-08), kèm ảnh form Tạo / giao việc: "Phần này làm theo dạng hiển thị avatar và tên nhân viên, tick chọn cho trực quan".
- Người chịu trách nhiệm chính: thẻ nhân viên (avatar chữ cái + tên + mã), chọn một (radio), thay cho ô thả xuống.
- Người cùng làm: cùng dạng thẻ, tick nhiều (checkbox); người đang là phụ trách chính hiện mờ, không tick được.
- Ô tìm theo tên/mã khi có hơn 8 nhân viên. Áp dụng cả form Việc lặp theo kỳ (người phụ trách).
- Không đổi dữ liệu gửi lên (Input.OwnerEmployeeId, Input.MemberIds), không đổi quyền.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `src/TAPortal.Web/Views/Work/_PeoplePicker.cshtml`
- MODIFY `src/TAPortal.Web/Models/WorkModels.cs`, `src/TAPortal.Web/Views/Work/Edit.cshtml`, `src/TAPortal.Web/Views/Work/TemplateEdit.cshtml`, `src/TAPortal.Web/wwwroot/css/ta-admin.css`, `src/TAPortal.Web/wwwroot/js/ta-work.js`, `tests/TACustomerPortal.Tests/SafeAdminWorkTests.cs`

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Không có migration.
- Execution: NOT_APPLICABLE

## Test và kết quả
- Release build 0 lỗi; Safe tests 595/595.

## Publish
- portal. Xem report.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: 2f4ae6a
- PR:
- Merge: fast-forward main
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Chưa xem trực tiếp giao diện trên production; operator mở Tạo / giao việc để xác nhận.
