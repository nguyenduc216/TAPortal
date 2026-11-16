# Change CONTRACT-REVIEW-CHG-20261114T143425Z-457FA8

- Feature ID: `CONTRACT-REVIEW`
- Change ID: `CONTRACT-REVIEW-CHG-20261114T143425Z-457FA8`
- Created UTC: 2026-11-14T14:34:25.799393+00:00
- Tiêu đề: In-place review: paper view, accept/edit/reject on highlights, direct paragraph edit, customer answers
- portal DEPLOYED lần 2 (ghi chú gửi AI) 2026-11-14 15:44:16Z từ ebb4f89; artifact bc1281ff63ee299bf359c18bada898f1e92b6b8e37c07b158b345f29bc103e82;
  package 4E48EDAAD0B398DC8D9D88128A204B8C5018ED61D34CEECF360258D13FF3AA72; health 200; Safe tests 771/771; khóa API trong appsettings vẫn giữ sau deploy.
- portal DEPLOYED lần 3 (tiến trình AI) 2026-11-14 16:01:54Z từ cda375f (đã kiểm tra 0 lượt AI đang chạy trước khi deploy);
  artifact ab6f7b1d6d6e5d7eb4d6e157c08bc191ab7c96b99ff0f51456711f63b5cdd692; package BC3DB68A00F64BC3F79EC0872205C5759DAD60073A1283895868C9F5BD3C8C85; health 200.
- portal DEPLOYED lần 4 (link luật, so sánh, token, đỏ, cuộn đúng chỗ) 2026-11-14 16:28:33Z từ bafd8b0 (0 lượt AI đang chạy trước khi deploy);
  artifact 46cc1ecb946d55ad1ecff9947e2179843dca157efbb96d9f112479434cd75eaf; package ABFE41EE85A25BA0CA8C9D19B4CFCF7FBB2593BB201233869FC1735D71F24478; health 200.
- portal DEPLOYED lần 5 (so sánh kèm gợi ý) 2026-11-14 17:03:10Z từ f04fbcc (gồm origin/main ad73524; 0 lượt AI đang chạy);
  artifact 9c59ff941ac69de3ed56f664996ff366f1f7ccb4365544ebd4bf4b50f19cf281; package 0A8F52334A7A568C96E042A1C6E72C236DEC35A63499C60696F485B2DDECB69E; health 200.
- portal DEPLOYED lần 6 (tô sáng khoản luật, link vbpl) 2026-11-14 17:16:25Z từ c8a33f6 (0 lượt AI đang chạy);
  artifact bd9e5906c4b2049464c6398f45bcdd4a94e1c3f130ec41c568344a406041892a; package F43854C90B0C7068F71E2C1AE25738568308BD0EE0BE76BA34FDBEDC39E6B1BE; health 200.
- portal DEPLOYED lần 7 2026-11-14 17:23:39Z từ c882ef0 = c8a33f6 + feature/tx-sync 6aec8c6 (ảnh nhân viên, migration 051) vì deploy 6aec8c6
  của phiên khác thiếu c8a33f6; artifact 5b6f6f9e90f188dca23300cf8b970578b46c1a0e55baa006911ce8032b053fad;
  package 1658D1E87DE95C13852C9ED6DD8E4B52004823260FBB404173F35328C37EAADF; health 200; Safe tests 775/775.
- Trạng thái: DEPLOYED

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

- Bổ sung (operator 2026-11-14 "Thêm ghi chú để AI hiểu nhé, không có ghi chú gì thì thôi"): ghi chú "Yêu cầu của khách hàng / ghi chú"
  được gửi cho AI (đã ẩn dữ liệu cá nhân) như yêu cầu ưu tiên rà soát; trống thì không thêm gì. ReviewAiRequest.CustomerNote,
  ReviewRunJob.Note, ContractReviewSql.RunReview đọc Note; gợi ý dưới ô ghi chú ở trang tạo yêu cầu.

- Bổ sung (operator 2026-11-14 "Có, thêm luôn"): khi AI đang chạy, trang hiện vòng xoay + thời gian đã chạy, tự hỏi
  GET ContractReviews/RunStatus/{id} mỗi 10 giây và tự cập nhật vùng làm việc khi xong (báo số góp ý hoặc lỗi); danh sách có
  vòng xoay cho yêu cầu "AI đang rà soát". Thời gian chạy đọc dạng UTC (ContractReviewsController.Utc).
- Lượt AI thật đầu tiên (đọc DB, không ghi): DONE, claude-opus-5-5, 15:50:46→15:53:27Z, 20 góp ý, 52.217/19.880 token, ~0,61 USD;
  15/20 góp ý có căn cứ khớp kho luật.

- Bổ sung (operator 2026-11-14 sau lượt chạy thật đầu tiên): (1) căn cứ pháp lý có link mở đúng điều trong Kho văn bản pháp luật
  (LegalCitation.Parse + ILegalLibraryStore.ResolveAsync; trang kho tự mở điều #a-<id>); số trang không có vì kho lưu theo điều;
  (2) chế độ "So sánh với bản gốc": hai cột bản gốc (v0) | bản đang chỉnh sửa (bản hiện tại + góp ý đã đồng ý), canh theo đoạn;
  (3) số token khi AI đang chạy (đọc chính xác, viết ước tính từ chữ đang nhận, ghi mỗi 5 giây) và tổng token/chi phí các lượt;
  (4) góp ý nghiêm trọng (HIGH) tô đỏ; (5) bấm P54/#f-… cuộn đúng chỗ (không đổi location.hash vì popstate làm shell tải lại trang về đầu).
  ILegalLibraryStore.ResolveAsync có cài đặt mặc định để fake của SafeLegalWatchTests không phải sửa.

- Bổ sung (operator qua phiên chính: "phiên bản so sánh không thấy cái gợi ý..."): chế độ So sánh giờ là bản gốc | bản theo MỌI gợi ý
  chưa bị bỏ qua (chưa xử lý + đã đồng ý; ContractReviewPolicy.Suggested bỏ từng gợi ý xung đột), dưới mỗi đoạn có lý do, căn cứ
  (link kho luật), nút Đồng ý / Bỏ qua / Đặt lại / Sửa câu đề xuất; mặc định chỉ hiện đoạn có gợi ý, nút Gợi ý trước/sau.

- Bổ sung (operator qua phiên chính: link điều luật phải mở file luật, cuộn tới đoạn được trích và tô sáng, hoặc mở link trên mạng):
  link trích dẫn mang ?k=<khoản>&d=<điểm>; trang kho luật hiển thị từng dòng (data-k/data-d), mở đúng điều, tô vàng khoản và tô đậm
  điểm được trích; thêm link "Văn bản gốc trên vbpl.vn" (LegalDocuments.SourceUrl của migration 048, chỉ https) và "Tìm điều này
  trên mạng" (Google). Prompt yêu cầu AI ghi clause dạng "khoản 2, điểm a". Việc "đã đối chiếu kho luật" vẫn chỉ kiểm tra văn bản
  và số điều có trong kho, chưa kiểm tra nội dung khoản có nói đúng như AI viết.

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
- portal DEPLOYED 2026-11-14 14:42:20Z (giờ server 21:42:20) từ 234fa94 (gồm merge origin/main 2316d7d có LEGAL-WATCH 048);
  artifact 17693dd4b0f2df5227f732599ad4b82a75942a6de0cbdf9a8f5a792871aaae8e (90 files); package 62E7E6A62E40C0385657B06779C00532989EBE15B876F36049EA0D814C2B4D14;
  health 200; /js/ta-review.js 200. Safe tests 771/771.
- Trạng thái: DEPLOYED
- Artifact SHA256:
- Smoke test:

## Git
- Branch:
- Commit SHA:
- PR:
- Merge: PENDING_APPROVAL
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
