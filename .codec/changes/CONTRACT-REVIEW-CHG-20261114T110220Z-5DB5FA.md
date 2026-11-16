# Change CONTRACT-REVIEW-CHG-20261114T110220Z-5DB5FA

- Feature ID: `CONTRACT-REVIEW`
- Change ID: `CONTRACT-REVIEW-CHG-20261114T110220Z-5DB5FA`
- Created UTC: 2026-11-14T11:02:20.186935+00:00
- Tiêu đề: Law library: upload Word/PDF/TXT, keep files, re-import, edit articles
- Trạng thái: DEPLOYED

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

- One-off data migration 047 (verify 083, rollback 084; KHÔNG commit, đã xóa khỏi working tree theo quy định): nạp kho luật từ file operator
  `documents/laws` – 29/2023/QH15 (83 điều, hiệu lực 01/08/2024 theo Luật 43/2024) và 43/2024/QH15 (5 điều), kèm file nguồn,
  CreatedBy marker A0000047-…-000000000047. 11:09:53Z dry-run PASS→rollback NOT_APPLIED; 11:11:47Z gates PASS (760/760);
  11:11:50Z real run VERIFY_STATUS=PASS → EXECUTED. Số 047/083/084 đã dùng, giữ chỗ.
- Chưa nạp được (cần file khác): 13 PDF là bản scan không có lớp chữ (123/2020/NĐ-CP, 38/2019, 45/2019, 59/2020 (2 phần), 91/2015 (2 bản),
  32-btc, 41-nd, 70/2025/NĐ-CP, 73-cp, 78/2021/TT-BTC, 84-cp-nq); 15224_l36qh.rtf (Luật Thương mại 36/2005) dùng phông TCVN3.

- One-off data migration 049 (verify 087, rollback 088; KHÔNG commit, đã xóa): nạp 4 văn bản tải từ nguồn miễn phí theo yêu cầu operator –
  91/2015/QH13 (689 điều, trích chữ từ .doc Sở Tư pháp Đắk Lắk), 36/2005/QH11 (VBHN 113/2025, 309 điều; Điều 102–116 đã bãi bỏ),
  45/2019/QH14 (VBHN 18/2026, 220 điều), 59/2020/QH14 (VBHN 67/2025 ghép 2 phần Công báo, 218 điều); marker A0000049-…-000000000049.
  11:39:20Z dry-run PASS→rollback NOT_APPLIED; 11:41:36Z gates PASS (760/760); 11:41:54Z real run VERIFY_STATUS=PASS → EXECUTED.
- Bộ tách điều (e4bf00f): tách điều bị dính trong một đoạn, chấp nhận nhảy số qua điều đã bãi bỏ khi là tiêu đề "Điều N. Tên".
- Tải không được: vbpl.moj.gov.vn và skhdt.daklak.gov.vn không phân giải tên miền; PDF Luật Quản lý thuế 2025 tại vmrcc.gov.vn trả 404.

- One-off data migration 050 (verify 089, rollback 090; KHÔNG commit, đã xóa): 108/2025/QH15 Luật Quản lý thuế 2025 (53 điều,
  Công báo số 38 ngày 22-01-2026, nguồn legalzone.vn), marker A0000050-…-000000000050. 11:48:17Z dry-run PASS→rollback NOT_APPLIED;
  -SkipGates vì mã nguồn không đổi kể từ lần gates PASS 11:41:36Z (chỉ thêm hồ sơ Codec); 11:48:22Z real run PASS → EXECUTED.

## Publish
- portal DEPLOYED 2026-11-14 11:08:51Z (giờ server 18:08:51) từ 817ae4e; artifact 4b806c4ea73f8fc7de31b03cb2d2554bd0e22b3915438942785c5783b2c36c58 (89 files);
  package 4CA9397914E99C9137E9B4AB42188BFE5823ECDFD58027CB3A8522B0D7490A6C; health 200.
- portal DEPLOYED lần 2 (bộ tách điều) 2026-11-14 11:45:41Z từ 0613b03; artifact 81318879d44ddcd750dbfb5d027475fec6d8e3dd10fecf52b060b07d9451344b;
  package 444ACD6392A4EFA788BC782ADD6B58540AC76F311D3071C8E1CD84987F0624E6; health 200. Audit WARN có sẵn: AngleSharp 0.17.1 (Moderate).
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
