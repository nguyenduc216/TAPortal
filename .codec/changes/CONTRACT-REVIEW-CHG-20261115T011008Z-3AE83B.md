# Change CONTRACT-REVIEW-CHG-20261115T011008Z-3AE83B

- Feature ID: `CONTRACT-REVIEW`
- Change ID: `CONTRACT-REVIEW-CHG-20261115T011008Z-3AE83B`
- Created UTC: 2026-11-15T01:10:08.150311+00:00
- Tiêu đề: Accept all suggestions; Word export marked on the customer's original file
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator 2026-11-15: "có button đồng ý hết và xuất ra file word giống bản gốc mà đã có gạch bỏ (gạch ngang), các phần thêm mới thay thế
thì tô khối màu vàng... comment các nội dung, phần nào thay thế đưa nội dung thay thế vào comment, không thay đổi cấu trúc file gốc".
- Nút "Đồng ý tất cả gợi ý" (cột Các bước và thanh So sánh): mọi gợi ý chưa xử lý (trừ ghi chú) của phiên bản hiện tại → ACCEPTED,
  một giao dịch, có audit CONTRACT_REVIEW_ACCEPT_ALL.
- "Xuất Word chỉnh lý" (ContractReviewWordExport): mở chính file DOCX gốc khách gửi, đánh dấu tại chỗ các gợi ý đã đồng ý/sửa:
  chữ đề xuất bỏ → w:strike (giữ nguyên chữ và định dạng); chữ mới/thay thế chèn ngay sau, tô w:highlight yellow, cùng định dạng;
  bỏ đoạn → gạch cả đoạn; thêm đoạn → đoạn mới sau đoạn đích (cùng pPr) tô vàng; mỗi chỗ có comment Word (mức độ, lý do, nội dung
  thay thế, căn cứ, ghi chú); ghi chú (NOTE) chưa xử lý thành comment. Câu trích nằm trên nhiều run được tách run chính xác.
  Chỉ ghi lại document.xml, comments.xml, document.xml.rels, [Content_Types].xml; mọi phần khác chép nguyên byte.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `src/TAPortal.Web/Services/ContractReviewWordExport.cs`
- MODIFY `src/TAPortal.Web/Services/ContractReviewStore.cs` (AcceptAllAsync), `ContractReviewDocx.cs` (TextOf internal)
- MODIFY `src/TAPortal.Web/Controllers/ContractReviewsController.cs` (AcceptAll, ExportMarked)
- MODIFY `src/TAPortal.Web/Views/ContractReviews/Details.cshtml`; `tests/TACustomerPortal.Tests/SafeContractReviewTests.cs`

## Migration SQL (KHÔNG tự chạy)
- Không có migration.
- Database: TAPortalSys
- Script / absolute path:
- Thứ tự chạy:
- Preflight / Verify / Rollback:
- Execution: NOT_EXECUTED

## Test và kết quả
- Release build 0 lỗi; Safe tests 776/776 PASS (test mới: câu trích qua 2 run, giữ in đậm, gạch đúng câu trích, tô vàng, comment,
  styles/header không đổi, rels/content types; trang: Đồng ý tất cả, xuất Word chỉnh lý).
- Thử trên file DOCX thật (Luật Thương mại hợp nhất, 17 phần): 3 thay đổi áp dụng, chỉ 4 phần được ghi lại, 14 phần chép nguyên byte,
  mọi phần XML hợp lệ, đọc lại được. Chưa mở thử bằng Microsoft Word (không tự động hóa được Word trên máy này).

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
