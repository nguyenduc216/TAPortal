# Change CONTRACT-REVIEW-CHG-20261114T110220Z-5DB5FA

- Feature ID: `CONTRACT-REVIEW`
- Change ID: `CONTRACT-REVIEW-CHG-20261114T110220Z-5DB5FA`
- Created UTC: 2026-11-14T11:02:20.186935+00:00
- Tiêu đề: Law library: upload Word/PDF/TXT, keep files, re-import, edit articles
- Trạng thái: MIGRATED

## Yêu cầu và phạm vi
Operator 2026-11-14: "thêm cái nút tải file đi để tôi có thể nạp tri thức và quản lý các file ... lần sau có thể update một file khác
để nó cập nhật lại kiến thức cũ". Kho văn bản pháp luật: tải file Word .docx / PDF có chữ / .txt (đọc trên máy chủ, không dùng AI),
lưu mọi file đã tải (file đang dùng + lịch sử, tải về được), cập nhật văn bản bằng file mới (thay toàn bộ điều), dùng lại file cũ,
sửa / xóa từng điều. Bộ tách điều chỉ nhận số điều tăng dần (dòng "Điều 12" lệch thứ tự là trích dẫn bị xuống dòng), bỏ tiêu đề
chương/mục, báo các điều bị thiếu. PDF bỏ số trang và dòng "CÔNG BÁO/Số..." và nối dòng bị ngắt giữa câu. PDF scan (không có lớp chữ),
.doc và RTF không đọc được (báo lỗi rõ ràng).

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `database/migrations/046-legal-library-files.sql`, `database/scripts/081-verify-legal-library-files.sql`, `database/scripts/082-rollback-legal-library-files.sql`
- MODIFY `src/TAPortal.Web/Services/LegalLibraryStore.cs` (LegalFileText, file history, reimport, article edit/delete); ADD `Services/ContractReviewWorker.cs` (moved)
- MODIFY `src/TAPortal.Web/Services/ContractReviewPolicy.cs` (LegalTextParser sequential + Missing)
- ADD `src/TAPortal.Web/Controllers/LegalLibraryController.cs` (moved out of ContractReviewsController.cs, new actions Upload/Reimport/UpdateArticle/DeleteArticle/Download)
- MODIFY `src/TAPortal.Web/Views/LegalLibrary/{Index,Edit,Details}.cshtml`
- MODIFY `src/TAPortal.Web/TAPortal.Web.csproj` (+ NuGet PdfPig 0.1.16, Apache-2.0), `THIRD-PARTY-NOTICES.md` (Anthropic 13.8.0 Apache-2.0, PdfPig)
- MODIFY `tests/TACustomerPortal.Tests/SafeContractReviewTests.cs` (+4 tests)

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path:
- Thứ tự chạy:
- Preflight / Verify / Rollback:
- Script: `database/migrations/046-legal-library-files.sql` (verify 081, rollback 082 – từ chối khi đã có file luật).
- Execution: EXECUTED qua Invoke-TAMigrations -Plan 046:081:082 (UTC 2026-11-14): 11:02:58 pre-check NOT_APPLIED; 11:02:59 dry-run
  PASS→rollback NOT_APPLIED, @@TRANCOUNT=0; backup full gần nhất 241 phút (không cần backup mới); 11:04:56 gates PASS (760/760);
  11:04:57 real run VERIFY_STATUS=PASS → EXECUTED

## Test và kết quả
- Release build 0 lỗi; Safe tests 760/760 PASS (17 test CONTRACT-REVIEW).
- Đọc thử bằng chính LegalFileText trên thư mục operator `documents/laws`: luat29.pdf (Luật KDBĐS 29/2023/QH15) tách 83/83 điều; 43-2024-qh15.pdf 5/5 điều; 13 PDF còn lại là bản scan (không có lớp chữ); 15224_l36qh.rtf dùng phông TCVN3 (.VnTime).

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
