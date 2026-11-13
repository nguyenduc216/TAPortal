# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261113T052254Z-6CA9E4

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261113T052254Z-6CA9E4`
- Created UTC: 2026-11-13T05:22:54.890317+00:00
- Tiêu đề: QR tu tao lai khi chet, chan trang email, menu theo trang dang mo
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator (2026-11-13):
- QR đơn MB quét báo "Truy vấn mã QR không thành công": get-status trả 1076 "Bill không tồn tại" dù mã tạo với hạn 5760 phút chưa đầy 14 giờ (lần trước cũng mất trong < 28 giờ). Yêu cầu biết hạn thật và tự xử lý.
- Chân email: "hoadontudong.vn - 1 sản phẩm của Tư vấn giải pháp T.A", link tới website T.A.
- Thanh menu bên trái không giữ/cuộn tới trang đang mở sau khi chuyển trang.

Đã làm:
- `OrderPayments`: ngay sau khi tạo QR động, hỏi trạng thái và so `expireInMinute` đối tác báo với hạn đã chọn; ngắn hơn thì ghi log `QR_LIFETIME_SHORTENED` và báo cho nhân viên. `EnsureLiveQrAsync`: trước khi hiện QR (trang thanh toán công khai, chi tiết đơn), tối đa 5 phút một lần mỗi đơn, hỏi trạng thái; 1076 hoặc expired thì tạo QR mới cho số còn phải thu, giữ nguyên hạn đã hẹn và link. "Kiểm tra thanh toán" gặp 1076 báo rõ và để lượt xem kế tiếp tạo lại.
- `TingeeBillInfo.ExpireInMinute`.
- `PortalEmailRenderer`: chân trang HTML và text như yêu cầu; AGENTS.md (quy tắc thương hiệu) ghi ngoại lệ này.
- `ta-shell.js` (portal và hóa đơn): menu giữ vị trí cuộn khi chuyển trang mềm và chỉ cuộn khi mục đang mở nằm ngoài vùng nhìn.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TACustomerPortal.Web/Services/OrderPayments.cs`, `src/TACustomerPortal.Web/Controllers/{OrdersController,PayController}.cs`
- MODIFY `src/TAPortal.Integrations/Tingee/TingeeApi.cs`, `src/TAPortal.Shared/PortalContent.cs`, `AGENTS.md`
- MODIFY `src/TAPortal.Web/wwwroot/js/ta-shell.js`, `src/TACustomerPortal.Web/wwwroot/js/ta-shell.js`
- ADD `tests/TACustomerPortal.Tests/SafeQrLivenessTests.cs`; MODIFY `tests/TACustomerPortal.Tests/{SafePortalContentTests,SafeCustomerSignupTests}.cs`

## Migration SQL (KHÔNG tự chạy)
- Không có.

## Test và kết quả
- Release build 0 lỗi; Safe tests 683/683. Trang thử dùng ta-shell.js thật: mở trang cuối danh sách thì menu cuộn tới mục; chuyển trang mềm giữ nguyên vị trí cuộn.

## Publish
- invoice, portal, webhooks. Xem report.

## Git
- Branch: feature/tx-sync
- Commit SHA: xem report
- PR:
- Merge: xem report
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Chưa biết hạn thật của QR động MB; đã hỏi kỹ thuật đối tác. Tự tạo lại mã chỉ chạy khi có người mở trang.
