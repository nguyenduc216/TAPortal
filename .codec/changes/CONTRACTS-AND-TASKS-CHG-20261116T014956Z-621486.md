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
