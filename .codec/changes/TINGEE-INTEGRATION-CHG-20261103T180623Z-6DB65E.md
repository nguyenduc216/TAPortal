# Change TINGEE-INTEGRATION-CHG-20261103T180623Z-6DB65E

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261103T180623Z-6DB65E`
- Created UTC: 2026-11-03T18:06:23.987413+00:00
- Tiêu đề: Show Tingee error details; tax code for business bank links in the sandbox
- Trạng thái: DEPLOYED (portal, invoice)

## Yêu cầu và phạm vi
Sandbox (Master, merchant con #322825 "Tư vấn giải pháp T.A" đã tạo thành công) liên kết MB Bank tài khoản doanh nghiệp → Tingee 400 "Data input invalid". Theo tài liệu error-list, chi tiết trường lỗi nằm trong phản hồi nhưng TAPortal chỉ hiện câu chung. Sửa: TingeeClient giữ phản hồi lỗi thô (≤2000 ký tự, kể cả khi `data` là danh sách lỗi không khớp kiểu), TingeeApiException.Details; Sandbox hiển thị và ghi log chi tiết. Sandbox thêm ô Mã số thuế (gửi `taxCode`) cho tài khoản doanh nghiệp/hộ kinh doanh, như Customer Portal đã làm.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TAPortal.Integrations/Tingee/{TingeeClient,TingeeApi}.cs`.
- MODIFY `src/TAPortal.Web/{Controllers/TingeeSandboxController.cs, Models/TingeeSandboxModels.cs, Views/TingeeSandbox/Index.cshtml}`.
- MODIFY tests `SafeTingeeDirectMerchantTests.cs` (+1), `SafeAdminTingeeSandboxTests.cs` (+1).

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path:
- Thứ tự chạy:
- Preflight / Verify / Rollback:
- Execution: NOT_APPLICABLE

## Test và kết quả
- Build Release 0 lỗi; Safe tests 326/326.

## Publish
- portal: DEPLOYED, source 5e81835, artifact 4ad62c2194961ed42fe7eeb47dd4498ec8d9cbf45c3a80bd005541f62b9a3047, package 5FF84E3B4D639D038643F341592DFE8AF47FBF2CEB0C229E202A96CAED5C4801, health 200.
- invoice: DEPLOYED, source 5e81835, artifact d1032a161c9a45fa4509266b30202f2c57b6d5eaeadd97d8830b5d30bddb32d1, package B652583592B951795AA877BAD3AF3A283E51EA4644712621FC8381245578FD22, health 200.

## Git
- Branch:
- Commit SHA:
- PR:
- Merge: PENDING_APPROVAL
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Chưa biết chắc nguyên nhân 400 của MB doanh nghiệp: cần operator thử lại để xem chi tiết. Catalog có ghi chú "Tài khoản doanh nghiệp MB cần T.A/Tingee cấu hình trước".
