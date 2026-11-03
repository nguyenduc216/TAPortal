# Change TINGEE-INTEGRATION-CHG-20261103T180623Z-6DB65E

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261103T180623Z-6DB65E`
- Created UTC: 2026-11-03T18:06:23.987413+00:00
- Tiêu đề: Show Tingee error details; tax code for business bank links in the sandbox
- Trạng thái: IMPLEMENTED / TESTS_PASS / DEPLOY_PENDING

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
- Chưa biết chắc nguyên nhân 400 của MB doanh nghiệp: cần operator thử lại để xem chi tiết. Catalog có ghi chú "Tài khoản doanh nghiệp MB cần T.A/Tingee cấu hình trước".
