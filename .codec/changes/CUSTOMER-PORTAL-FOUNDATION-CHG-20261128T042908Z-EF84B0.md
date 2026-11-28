# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261128T042908Z-EF84B0

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261128T042908Z-EF84B0`
- Created UTC: 2026-11-28T04:29:08.170489+00:00
- Tiêu đề: Order details: progress timeline and paid state
- Trạng thái: DEPLOYED

## Yêu cầu và phạm vi
Operator (2026-11-28), trang chi tiết đơn:
- Đơn đã chuyển khoản đủ: ô QR thay bằng icon "đã chuyển khoản thành công".
- Thêm timeline tiến độ đơn: tạo đơn → thanh toán (số lần, đủ hay chưa) → khớp số tiền → báo khách đã nhận tiền → xuất hóa đơn điện tử (nếu tự động hoặc đã xuất) → gửi hóa đơn cho khách. Xong là xanh, cần xem là vàng.

Quyết định:
- Dữ liệu lấy từ bảng có sẵn, không migration:
  - Thanh toán: PaymentTransactions (MATCHED/REVIEW/RESOLVED) của đơn, cộng phần chia qua phiếu thu (PaymentAllocations).
  - Báo khách: NotificationOutbox, Scope CUSTOMER, kênh SMTP/ZALO_OA của các giao dịch đó. Xem được nội dung tin.
- Trạng thái mỗi bước: Done (xanh), Warn (vàng: trả dư, lệch đối soát, tin lỗi, hóa đơn nháp/lỗi), Todo (chưa tới), Off (không áp dụng).
- Chữ "Đã gửi email cho người mua" đổi thành "Đã yêu cầu {nhà cung cấp} gửi email": hệ thống chỉ nhờ nhà cung cấp gửi, không có xác nhận đã tới hộp thư.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `src/TACustomerPortal.Web/Models/OrderTimeline.cs`
- MODIFY `src/TACustomerPortal.Web/Services/PaymentStore.cs` (OrderActivityAsync, OrderPayment, OrderNotice)
- MODIFY `src/TACustomerPortal.Web/Models/PaymentModels.cs`, `Controllers/OrdersController.cs`, `Views/Orders/Details.cshtml`, `wwwroot/css/customer.css`, `wwwroot/js/customer.js`
- MODIFY `tests/TACustomerPortal.Tests/SafeCustomerEInvoiceTests.cs`, `SafeCustomerPaymentsTests.cs`

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Không có migration.
- Execution: NOT_APPLICABLE

## Test và kết quả
- Build 0 lỗi. Safe tests 870 PASS (test mới APaidOrderShowsItsTimelineAndNoQr).

## Publish
- Project: TACustomerPortal.Web
- Destination (chỉ từ config): invoice (TA.Invoice)
- Trạng thái: DEPLOYED 2026-11-28 04:32Z, source commit fbbc603, `Deploy-TARemote.ps1 -Target invoice`, health HTTP 200
- Artifact SHA256: 92401457754a10cc31b48870ec0a7d5e693b4f5dc4c85b70f3fe431fb8a451cb (package 122DA24E27A108E4655F02761A99D66A0A5FFD8D08400C1BDAE5D03DCC46D274)
- Backup trên server: `C:\TADeploy\backups\invoice-20261128-113236`
- Smoke test: health 200; audit gates PASS.

## Git
- Branch: feature/order-details-popups
- Commit SHA: fbbc603
- PR:
- Merge: MERGED fast-forward vào main (sau deploy)
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
