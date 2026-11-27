# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261127T165527Z-02BE59

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261127T165527Z-02BE59`
- Created UTC: 2026-11-27T16:55:27.038166+00:00
- Tiêu đề: Order details: customer info and invoice email dialogs
- Trạng thái: PLANNED

## Yêu cầu và phạm vi
Operator (2026-11-27), trang chi tiết đơn: bấm tên khách hàng để xem thông tin khách (popup); bấm "Đã gửi email cho người mua" để biết đã gửi gì, gửi cho ai.

Quyết định:
- Tên khách là nút mở hộp thoại thông tin khách: mã, tên đơn vị, MST, điện thoại, Zalo, email, địa chỉ, tài khoản ngân hàng, ghi chú; OWNER/ADMIN có nút "Sửa thông tin". Khách lẻ giữ chữ thường.
- "Xem email đã gửi" mở hộp thoại: thời điểm gửi, gửi tới (email khách đang lưu, có ghi chú nếu đã đổi sau đó), người gửi (nhà cung cấp hóa đơn), nội dung (thông báo phát hành hóa đơn số …), nút xem hóa đơn PDF.
- Email do nhà cung cấp hóa đơn điện tử gửi theo mẫu của họ; hệ thống không lưu bản sao thư nên nói rõ điều đó thay vì dựng lại nội dung. Không thêm migration.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TACustomerPortal.Web/Controllers/OrdersController.cs` (tên nhà cung cấp hóa đơn vào trang)
- MODIFY `src/TACustomerPortal.Web/Models/PaymentModels.cs` (OrderDetailsVm.InvoiceProvider)
- MODIFY `src/TACustomerPortal.Web/Views/Orders/Details.cshtml` (hai hộp thoại)
- MODIFY `src/TACustomerPortal.Web/wwwroot/css/customer.css` (`.cp-dl--stack`, `.cp-linkbtn`)
- MODIFY `src/TACustomerPortal.Web/wwwroot/js/customer.js` (mở/đóng hộp thoại `data-cp-show-dialog`)
- MODIFY `tests/TACustomerPortal.Tests/SafeCustomerEInvoiceTests.cs`

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Không có migration.
- Execution: NOT_APPLICABLE

## Test và kết quả
- `dotnet build`: 0 lỗi.
- Safe tests: 869 PASS (test mới OrderDetailsOpensTheCustomerAndTheInvoiceEmailSent).

## Publish
- Project: TACustomerPortal.Web
- Destination (chỉ từ config):
- Trạng thái: NOT_PUBLISHED
- Artifact SHA256:
- Smoke test:

## Git
- Branch: feature/order-details-popups (cắt từ main 639bf12)
- Commit SHA:
- PR:
- Merge: PENDING_APPROVAL
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- "Gửi tới" là email khách đang lưu, không phải địa chỉ ghi lại lúc gửi. Muốn chính xác tuyệt đối cần lưu địa chỉ khi gửi (thêm cột), chưa làm.
