# Change CONTRACT-REVIEW-CHG-20261114T143425Z-457FA8

- Feature ID: `CONTRACT-REVIEW`
- Change ID: `CONTRACT-REVIEW-CHG-20261114T143425Z-457FA8`
- Created UTC: 2026-11-14T14:34:25.799393+00:00
- Tiêu đề: In-place review: paper view, accept/edit/reject on highlights, direct paragraph edit, customer answers
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator 2026-11-14: trên giao diện web hiển thị nội dung hợp đồng và các chỗ sửa tô vàng ngay trên văn bản, người dùng bấm OK đồng ý
với đề xuất thì đổi màu, các tương tác khác cũng làm ngay trên văn bản (không chỉ tải Word).
- Trang làm việc: hợp đồng hiển thị dạng trang giấy (như Word). Góp ý mở: tô vàng; đã chấp nhận: chữ cũ gạch đỏ + chữ mới gạch dưới
  xanh tại chỗ; bỏ qua: gạch chấm xám; đoạn đề xuất thêm hiện thành đoạn "mới".
- Bấm vào chỗ tô vàng → bảng tại chỗ (template#pop-<id>, wwwroot/js/ta-review.js): lý do, căn cứ, câu đề xuất (so sánh từng từ),
  nút Đồng ý / Bỏ qua / Đặt lại, sửa câu đề xuất rồi đồng ý. Form gửi bằng fetch, chỉ vùng làm việc được thay (không nhảy trang).
- ✎ trên mỗi đoạn: sửa trực tiếp → ghi thành góp ý "nhân viên sửa" đã chấp nhận (action EditParagraph), bản hiện tại không đổi.
- Chế độ "Thay đổi so với bản trước" khi chờ khách phản hồi: bấm chỗ sửa → Khách đồng ý / không đồng ý / đề xuất câu khác.
- Không JS: danh sách góp ý và thay đổi bên dưới vẫn dùng được như trước.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TAPortal.Web/Models/ContractReviewModels.cs` (ReviewHtml: hiển thị quyết định tại chỗ, nút data-ta-pop)
- MODIFY `src/TAPortal.Web/Views/ContractReviews/Details.cshtml` (trang giấy, template bảng quyết định)
- MODIFY `src/TAPortal.Web/Controllers/ContractReviewsController.cs` (EditParagraph)
- ADD `src/TAPortal.Web/wwwroot/js/ta-review.js`; MODIFY `wwwroot/css/ta-review.css`, `Views/Shared/_Layout.cshtml` (nạp script)
- MODIFY `tests/TACustomerPortal.Tests/SafeContractReviewTests.cs`

## Migration SQL (KHÔNG tự chạy)
- Không có migration.
- Database: TAPortalSys
- Script / absolute path:
- Thứ tự chạy:
- Preflight / Verify / Rollback:
- Execution: NOT_EXECUTED

## Test và kết quả
- Release build 0 lỗi; Safe tests 760/760 PASS. Kiểm tra trực quan bằng bản HTML render từ test (dữ liệu giả) trong trình duyệt: trang giấy, tô vàng, bảng Đồng ý/Bỏ qua mở đúng chỗ, nội dung được mã hóa (chuỗi <script> hiện dạng chữ). Chưa kiểm tra trên portal thật sau đăng nhập.

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
