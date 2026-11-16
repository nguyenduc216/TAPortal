# Feature: LEGAL-WATCH

- Feature ID: `LEGAL-WATCH`
- Tên: Văn bản mới cần duyệt — theo dõi văn bản pháp luật thuế cho Kho văn bản pháp luật (Hợp đồng dịch vụ)
- Mục tiêu: tự phát hiện văn bản thuế mới (Công báo, hằng tuần) và thay đổi hiệu lực của văn bản trong kho (vbpl.vn, hằng tháng),
  đưa vào hàng chờ để người có quyền LEGAL.LIBRARY.MANAGE duyệt trước khi vào kho; email tổng hợp hằng tháng.
- Phạm vi: luật, nghị định, thông tư, nghị quyết… đăng Công báo có từ khóa thuế trong trích yếu. Ngoài phạm vi: công văn
  (gdt.gov.vn), thuvienphapluat.vn, mof.gov.vn, vbpl.vn /api/, tóm tắt bằng Claude.
- Kiến trúc / bảng: dbo.LegalWatchRuns, dbo.LegalWatchItems, dbo.LegalDocuments.SourceUrl/EffectStatus/StatusCheckedAt (migration 048);
  `Services/LegalWatchSources.cs` (readers), `LegalWatchStore.cs`, `LegalWatchService.cs` (HTTP allow-list, runs, scheduler),
  `Controllers/LegalWatchController.cs`, `Views/LegalWatch/Index.cshtml`.
- Phụ thuộc: CONTRACT-REVIEW (dbo.LegalDocuments/LegalArticles/LegalDocumentFiles, SqlLegalLibraryStore.SaveAsync, LegalFileText),
  Notifications (system SMTP channel).
- Trạng thái: ACTIVE

## Change history

| Change ID | Ngày UTC | Nội dung | Commit SHA | Migration | Publish | Merge |
|---|---|---|---|---|---|---|
| `LEGAL-WATCH-CHG-20261114T113930Z-7A55AD` | 2026-11-14 | Công báo weekly + vbpl.vn monthly watch, review queue, monthly email | aeb8a91 | 048 EXECUTED | DEPLOYED portal 5871b46 | MERGED |
