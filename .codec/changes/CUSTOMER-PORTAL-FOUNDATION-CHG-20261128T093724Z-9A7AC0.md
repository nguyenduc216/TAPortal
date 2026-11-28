# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261128T093724Z-9A7AC0

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261128T093724Z-9A7AC0`
- Created UTC: 2026-11-28T09:37:24.531111+00:00
- Tiêu đề: SoftDreams production connect: code 111 is a valid login; lookup sends the pattern
- Trạng thái: PLANNED

## Yêu cầu và phạm vi
Operator (2026-11-28): kết nối môi trường chính thức SoftDreams (api.easyinvoice.vn) báo "Nhà cung cấp hóa đơn từ chối (mã 111): Mẫu không khả dụng".
- Nguyên nhân: bước kiểm tra đăng nhập tra cứu một Ikey giả qua getInvoicesByIkeys mà không gửi Pattern. Môi trường demo trả 128 (Ikey không tồn tại), môi trường chính thức trả 111. Cả hai mã đều đến sau khi tài khoản được nhận diện.
- Sửa: bước kết nối coi mã 111 là đăng nhập hợp lệ (ghi log EINVOICE_CONNECT_PATTERN_UNAVAILABLE); khách thêm mẫu cho môi trường chính thức sau khi kết nối.
- getInvoicesByIkeys nhận thêm Pattern (tùy chọn); lần tra cứu trước khi xuất hóa đơn gửi mẫu đang dùng, để môi trường chính thức không trả 111 ở bước xuất.
- Mật khẩu operator gửi trong chat không được dùng hay ghi lại; operator tự nhập trên trang.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TAPortal.Integrations/SoftDreams/EasyInvoice.cs`, `src/TAPortal.Integrations/Tingee/OrderInvoices.cs`
- MODIFY `src/TACustomerPortal.Web/Controllers/EInvoiceController.cs`
- MODIFY `tests/TACustomerPortal.Tests/SafeEasyInvoiceTests.cs`

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path: không có
- Thứ tự chạy:
- Preflight / Verify / Rollback:
- Execution: NOT_EXECUTED

## Test và kết quả
- Safe tests 874 PASS (test mới ThePatternUnavailableAnswerIsAValidLoginAndTheLookupSendsThePattern; test chuyển môi trường có thêm tình huống 111). Build solution Release 0 lỗi.

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
