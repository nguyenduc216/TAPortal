# CONTRACT-REVIEW — Hợp đồng dịch vụ (chỉnh lý hợp đồng có AI rà soát)

Operator 2026-11-14: dịch vụ pháp lý "chỉnh lý hợp đồng" cho khách hàng. Nhân viên tạo yêu cầu chỉnh lý theo từng khách hàng,
tải file Word của khách; AI (Claude API, không dùng 79AI vì 79AI chỉ có công cụ ảnh/video/nhạc) rà soát cho Bên A, Bên B hoặc
cả hai, chỉ ra điều khoản sai/bất lợi kèm căn cứ pháp lý và câu chữ đề xuất. Nhân viên duyệt từng góp ý; mỗi vòng sửa là một
phiên bản mới, bản gốc không bao giờ bị ghi đè. Bản sửa xuất ra Word có theo dõi thay đổi thật (w:ins/w:del + comment) để gửi
khách; ý kiến khách cho từng thay đổi tạo phiên bản tiếp theo; cuối cùng chốt bản cuối.

## Giai đoạn
1. TAPortal (giai đoạn này): yêu cầu, file gốc, phiên bản, AI rà soát, góp ý thủ công, duyệt, track changes, xuất Word,
   ghi nhận ý kiến khách (nhân viên nhập hộ), chốt bản cuối, kho văn bản pháp luật.
2. Hóa đơn tự động: khách tự xem và đồng ý/không đồng ý từng đoạn (không nêu tên nhà cung cấp AI – thêm vào `PartnerText.Names`).
3. Thư viện mẫu hợp đồng, tra cứu trên website TA.

## Kiến trúc
- Migration `database/migrations/044-contract-review.sql` (verify 077, rollback 078; rollback từ chối khi đã có dữ liệu).
- Bảng: `ContractReviews` (mã CLnnnnnn, `NumberSequences.CONTRACT_REVIEW_CODE`), `ContractReviewFiles`, `ContractReviewVersions`
  (v0 = bản gốc), `ContractReviewParagraphs` (ParagraphKey P1..Pn ổn định qua các phiên bản, Nn cho đoạn thêm),
  `ContractReviewRuns`, `ContractReviewFindings`, `ContractReviewChanges`, `LegalDocuments`, `LegalArticles`.
- Quyền: `CONTRACTS.REVIEW.VIEW/CREATE/RUN_AI/EDIT/FINALIZE`, `LEGAL.LIBRARY.MANAGE` (cấp cho SYS_ADMIN; vai trò khác gán ở
  Vai trò & phân quyền). FINALIZE = người duyệt bản cuối.
- Code: `Services/ContractReviewDocx.cs` (đọc DOCX không dùng thư viện ngoài, DTD bị chặn; so sánh theo từ; ghi DOCX track
  changes), `ContractReviewPolicy.cs` (quy tắc áp dụng góp ý, phản hồi khách, ẩn dữ liệu cá nhân, tách điều luật),
  `ContractReviewAi.cs` (prompt, JSON schema, kiểm tra câu trả lời, gọi Claude qua NuGet `Anthropic`), `ContractReviewStore.cs`,
  `LegalLibraryStore.cs` (+ `ContractReviewWorker` chạy hàng đợi AI), `Controllers/ContractReviewsController.cs`.
- AI: mô hình mặc định `claude-opus-5-5`, structured output JSON, streaming, adaptive thinking (effort high). Mọi góp ý phải trỏ
  đúng đoạn, câu trích phải có nguyên văn trong đoạn (không thì thành ghi chú), căn cứ pháp lý đối chiếu kho luật
  (`LegalVerified`). Số điện thoại, email, CCCD, số tài khoản được thay bằng ký hiệu trước khi gửi (mặc định bật).
- Cấu hình (operator tự đặt trên server, không commit): `ContractReview:Anthropic:ApiKey`; tùy chọn `ContractReview:Model`,
  `ContractReview:Effort`, `ContractReview:MaxOutputTokens`, `ContractReview:PriceInputPerMTok`, `ContractReview:PriceOutputPerMTok`.
  Chưa có key: trang vẫn chạy, nút AI bị tắt, góp ý thủ công vẫn dùng được.

## Quyết định còn chờ operator
- Danh sách luật ban đầu trong kho (gợi ý hiển thị trên trang Kho văn bản pháp luật).
- PDF: giai đoạn 1 chỉ nhận DOCX (để giữ khóa đoạn và xuất track changes chính xác).
- Tên hiển thị trợ lý trên trang khách hàng (giai đoạn 2).
