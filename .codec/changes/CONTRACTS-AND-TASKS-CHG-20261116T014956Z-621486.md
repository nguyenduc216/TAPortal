# Change CONTRACTS-AND-TASKS-CHG-20261116T014956Z-621486

- Feature ID: `CONTRACTS-AND-TASKS`
- Change ID: `CONTRACTS-AND-TASKS-CHG-20261116T014956Z-621486`
- Created UTC: 2026-11-16T01:49:56.966046+00:00
- Tiêu đề: Hoàn thành: checklist không bắt buộc, bắt buộc ghi chú kết quả
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

## Thực hiện
- Operator 2026-11-16 (ảnh nhân viên báo không chuyển được sang Hoàn thành vì chưa có checklist): "không cần có checklist vẫn cho hoàn thành, nhưng ít nhất phải có ghi chú khi hoàn thành".
- `WorkTaskPolicy.CheckDone`: checklist không bắt buộc (0 mục là đạt), mục nào đã có vẫn phải tích hết. `DoneNote`/`DoneNoteRequired`: ghi chú kết quả bắt buộc khi chuyển sang DONE.
- `WorkTaskStore.SetStatusAsync(..., note)`: thiếu ghi chú → Invalid; ghi chú lưu thành trao đổi "Hoàn thành: ..." cùng transaction với đổi trạng thái.
- UI (_TaskCard, Details): chọn "Hoàn thành" mở ô "Ghi chú kết quả" + nút "Xác nhận hoàn thành"/"Hủy" thay vì gửi ngay (ta-work.js); CSS `.ta-task__done-note`.
- Tests: SafeAdminWorkTests cập nhật (CheckDone(0,0)=Saved, DoneNote, thẻ có ô ghi chú). Safe 779/779 pass. Không đổi DB.

## Checklist mẫu của đầu việc (cùng change)
- Operator 2026-11-16: "danh mục công việc phải có cài đặt danh sách checklist, khi tạo mới công việc tự kéo các checklist đó ra, và bổ sung checklist mới được từ chi tiết 1 công việc đang làm hoặc trong danh mục công việc".
- Migration 056-work-catalog-checklist.sql (+099 verify, 100 rollback): `dbo.WorkTaskCatalog.ChecklistText nvarchar(4000) NULL`.
- `WorkCatalogPolicy.NormalizeChecklist`/`Append`; `WorkCatalogStore` lưu checklist mẫu khi thêm/sửa đầu việc, `AppendChecklistAsync` (cần TASKS.CATALOG.MANAGE, audit WORK_CATALOG_CHECKLIST_ADDED).
- Danh mục công việc: cột "Checklist mẫu" (nút mở ô soạn, mỗi dòng một mục) + ô checklist ở form thêm.
- Tạo công việc / việc lặp: chọn đầu việc trong gợi ý thì checklist mẫu tự điền vào ô Checklist (không đè nội dung người dùng tự gõ).
- Chi tiết công việc: người có quyền quản lý danh mục thấy nút "Bổ sung vào checklist mẫu của đầu việc" (WorkCatalog/FromTask) – thêm các mục của việc này mà mẫu chưa có.
- Tests: SafeWorkCatalogTests.EachEntryCarriesAChecklistTemplate. Safe 780/780 pass.
- Migration 056: pre-check NOT_APPLIED; dry-run (rolled back) afterMigration=PASS afterRollback=NOT_APPLIED trancount0=True; gates PASS (780 Safe); real run 2026-11-16 02:00:17Z VERIFY_STATUS=PASS ⇒ EXECUTED.
- Deploy 1 (quy tắc ghi chú hoàn thành, 62477f7): portal DEPLOYED, merge main.

## Diễn giải từng mục checklist mẫu (cùng change)
- Operator 2026-11-16: "thêm diễn giải cho checklist ... quy trình/công thức", chọn "Gắn từng mục".
- Migration 057-work-catalog-checklist-guides.sql (+101 verify, 102 rollback): `dbo.WorkTaskCatalog.ChecklistJson nvarchar(max) NULL` = [{"title","guide"}]; ChecklistText (056) vẫn giữ tên mục cho form tạo việc.
- `WorkCatalogPolicy.Steps/CleanSteps/ToJson/ToText/Append`; `WorkGuideIndex` + `IWorkCatalogStore.GuidesAsync` (cache 5 phút, xóa cache khi lưu/xóa/bổ sung; lỗi DB → rỗng).
- Danh mục công việc: ô soạn checklist mẫu từng mục (tên + diễn giải nhiều dòng, thêm/bỏ mục, đánh số); nút Checklist mẫu đang mở có màu nổi; "Số việc" là link sang Danh sách công việc lọc theo tên đầu việc (đếm cả việc lặp "tên — kỳ ...").
- Thẻ công việc và chi tiết công việc: mục có diễn giải hiện "Hướng dẫn" (mở/đóng) ngay dưới mục; tra theo tên đầu việc (cả việc lặp) và tên mục.
- Tests: SafeWorkCatalogTests.EachTemplateItemMayCarryAnExplanation. Safe 781/781 pass.
- Migration 057: pre-check NOT_APPLIED; dry-run PASS/NOT_APPLIED trancount0=True; gates PASS (781); real run 2026-11-16 02:23:39Z VERIFY_STATUS=PASS ⇒ EXECUTED.
