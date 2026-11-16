# Change CONTRACTS-AND-TASKS-CHG-20261116T071620Z-30C6CF

- Feature ID: `CONTRACTS-AND-TASKS`
- Change ID: `CONTRACTS-AND-TASKS-CHG-20261116T071620Z-30C6CF`
- Created UTC: 2026-11-16T07:16:20.408551+00:00
- Tiêu đề: Danh mục công việc: lưới dạng chữ, sửa nhanh trên lưới, popup 2 tab, kéo thả checklist
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
- Yêu cầu: "Giao diện khó dùng quá, chuyển về dạng bình thường. Hiển thị danh sách dạng text, edit/thêm mới mở popup, trong popup có tab thông tin chung và tab checklist. Trong list có thể để dạng update trực tiếp trên lưới. Và cho phép kéo thả lại vị trí sắp xếp trong checklist của công việc".
- Lưới: Tên / Mô tả / Thứ tự hiển thị dạng chữ; bấm đúp (hoặc Enter/F2) để sửa nhanh, Enter lưu, Esc bỏ; công tắc Đang dùng lưu ngay. Sửa (bút chì) và "Thêm đầu việc" (nút trên đầu trang) mở popup.
- Popup (_CatalogDialog.cshtml): tab Thông tin chung (tên, mô tả, thứ tự, đang dùng) và tab Checklist (mục + diễn giải, thêm mục, gợi ý từ các việc đang có). Kéo tay cầm để đổi thứ tự (chuột / cảm ứng) hoặc chọn tay cầm rồi ↑/↓. Hủy / Esc hỏi rồi trả lại như lúc tải trang.
- Sửa nhanh trên lưới gửi chính form của popup nên đi qua cùng WorkCatalogController.Save (không thêm endpoint).
- ADD src/TAPortal.Web/Views/WorkCatalog/_CatalogDialog.cshtml; MODIFY Views/WorkCatalog/Index.cshtml, wwwroot/js/ta-work.js, wwwroot/css/ta-admin.css, Models/WorkModels.cs (WorkCatalogDialogVm), TAPortal.Shared/UiIcon.cs (grip-vertical), tests SafeWorkCatalogTests.cs + SafeAdminWorkTests.cs (render test).
- Không có migration. Safe tests 783/783. Đã thử tương tác trên bản HTML render từ test (kéo thả, gợi ý, Hủy, sửa nhanh, bật/tắt, bắt buộc tên khi đang ở tab Checklist, màn hình điện thoại).
