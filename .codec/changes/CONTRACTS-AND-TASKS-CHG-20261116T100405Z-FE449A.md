# Change CONTRACTS-AND-TASKS-CHG-20261116T100405Z-FE449A

- Feature ID: `CONTRACTS-AND-TASKS`
- Change ID: `CONTRACTS-AND-TASKS-CHG-20261116T100405Z-FE449A`
- Created UTC: 2026-11-16T10:04:05.060163+00:00
- Tiêu đề: Công việc: ảnh đại diện trong lịch sử, tab Tài liệu (upload ảnh/Word/PDF/Excel kèm ghi chú)
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
- Yêu cầu: "Hiển thị logo của nhân viên đang thực hiện công việc. Bổ sung thêm quản lý tài liệu cho công việc, hỗ trợ upload file ảnh, word, pdf, excel và ghi chú nội dung cho tài liệu. chia tab với phần trao đổi và lịch sử".
- Trao đổi và lịch sử: mỗi dòng có ảnh đại diện người viết (ảnh nhân viên, không có ảnh thì chữ cái đầu).
- Tab Tài liệu cạnh tab Trao đổi và lịch sử: tải lên JPG/PNG/GIF/WEBP, DOC/DOCX, PDF, XLS/XLSX (≤ 10 MB, kiểm chữ ký nội dung, tối đa 50 tài liệu/việc) kèm ghi chú nội dung; danh sách có ảnh thu nhỏ / biểu tượng loại file, người tải, thời gian, dung lượng, ghi chú; tải về (attachment, nosniff; ảnh xem trực tiếp); người tải lên hoặc người sửa được việc thì sửa ghi chú / xóa (xóa mềm). Mỗi thao tác ghi vào lịch sử việc.
- Migration 058-work-task-documents.sql (dbo.WorkTaskDocuments), verify 103, rollback 104.
- ADD Services/WorkDocumentStore.cs, migration 058, scripts 103/104; MODIFY Controllers/WorkController.cs, Models/WorkModels.cs, Program.cs, Services/WorkTaskStore.cs (ActorId của lịch sử), Views/Work/Details.cshtml, wwwroot/css/ta-admin.css, wwwroot/js/ta-work.js, tests SafeAdminWorkTests.cs. Safe tests 786/786.
