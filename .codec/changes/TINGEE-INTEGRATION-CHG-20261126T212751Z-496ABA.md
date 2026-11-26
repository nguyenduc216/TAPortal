# Change TINGEE-INTEGRATION-CHG-20261126T212751Z-496ABA

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261126T212751Z-496ABA`
- Created UTC: 2026-11-26T21:27:51Z
- Tiêu đề: EasyInvoice: unknown Ikey (ErrorCode 128) means no invoice; neutral login hint
- Trạng thái: DEPLOYED

## Yêu cầu và phạm vi
Operator (2026-11-27, ảnh trang Hóa đơn điện tử): "Lỗi khi kết nối". Kết nối SoftDreams báo "Nhà cung cấp hóa đơn từ chối (mã 128): Ikeys không tồn tại trong hệ thống".

Nguyên nhân (HIGH): hệ thống thật của EasyInvoice trả ErrorCode 128 khi tra `getInvoicesByIkeys` một Ikey chưa có hóa đơn, thay vì danh sách rỗng. Lỗi này đến sau khi login đã được chấp nhận.
- Bước kiểm tra kết nối (tra một Ikey ngẫu nhiên) báo lỗi dù login đúng.
- Bước xuất hóa đơn (`IssueDirect` tra Ikey trước khi phát hành) cũng sẽ bị chặn với mọi đơn mới.

Sửa:
- `EasyInvoiceApi.GetByIkeysAsync`: ErrorCode 128 → danh sách rỗng (`EasyInvoiceResponse.IkeyNotFoundCode`, `IsIkeyNotFound`). Mọi lỗi khác vẫn ném như cũ.
- View Hóa đơn điện tử: ghi chú dưới ô tên đăng nhập trước chỉ nói về Viettel, nay viết chung cho mọi nhà cung cấp.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TAPortal.Integrations/SoftDreams/EasyInvoice.cs`
- MODIFY `src/TACustomerPortal.Web/Views/EInvoice/Index.cshtml`
- MODIFY `tests/TACustomerPortal.Tests/SafeEasyInvoiceTests.cs` (gọi `EasyInvoiceApi` qua HttpMessageHandler giả: mã 128 → rỗng, số hoặc chuỗi; lỗi khác vẫn ném)

## Migration SQL
- Không có. Execution: NOT_APPLICABLE

## Test và kết quả
- Release build 0 lỗi; Safe tests 861/861 PASS.

## Publish
- Project: TAPortal.Web, TACustomerPortal.Web, TAPortal.Webhooks (Integrations thay đổi)
- Trạng thái: xem report.

## Git
- Branch: feature/einvoice-softdreams; merge main sau deploy.

## Rủi ro, quyết định và việc còn lại
- Lần thử kết nối của operator đi tới máy chủ production `api.easyinvoice.vn` (ô demo không tích) và login được chấp nhận.
- Operator kết nối lại rồi thử xuất hóa đơn cho đơn thử.
