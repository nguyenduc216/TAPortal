# Change TINGEE-INTEGRATION-CHG-20261105T010435Z-CEA3FA

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261105T010435Z-CEA3FA`
- Tiêu đề: Webhooks: apply Tingee payment webhooks to customer orders in the background
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator (2026-11-05): tắt trang đơn hàng thì chuyển khoản không được ghi nhận (số tiền, trạng thái).

Nguyên nhân (audit): webhook thanh toán chỉ được lưu vào `dbo.TingeeWebhookInbox` (Status RECEIVED), không có gì áp vào đơn. Số đã thu chỉ đổi khi người dùng bấm "Kiểm tra thanh toán" (đọc trạng thái QR động từ Tingee); trang chi tiết chỉ đọc lại DB mỗi 10 giây. Đơn QR tĩnh không bao giờ được cập nhật. Mức: HIGH (tính năng thiếu, không mất dữ liệu: webhook vẫn được lưu).

Sửa:
- `TingeePaymentNotice` (parse webhook: transactionCode, amount, content, accountNumber, vaAccountNumber, type, additionalData), `TingeePaymentMatcher` (billId trong additionalData → mã đơn trong nội dung CK chỉ chữ+số, ưu tiên mã dài nhất → nếu ngân hàng không gửi nội dung (VietinBank): đơn duy nhất còn nợ đúng số tiền).
- `TingeePaymentProcessor`: QR động → đọc số đã trả từ Tingee (cùng quy tắc "Kiểm tra thanh toán", không cộng trùng; Tingee chưa cập nhật thì thử lại tối đa 3 lần); QR tĩnh → cộng số tiền webhook; đủ tiền → PAID, PaidAt, InvoiceStatus PENDING (nếu tự xuất HĐ), đóng QR động (best effort). Không khớp → UNMATCHED + lý do; debit → IGNORED; body lỗi → FAILED; lỗi tạm thời → thử lại tối đa 10 lần.
- `SqlTingeePaymentLedger`: đổi trạng thái inbox và đơn trong một transaction, có kiểm tra PaidAmount cũ (không cộng hai lần, không đè thao tác đồng thời). Chỉ khớp với đơn tạo trước thời điểm nhận webhook (webhook cũ trong inbox không trả cho đơn mới).
- `TingeePaymentWorker` (BackgroundService, 5 giây/lần) đăng ký trong TAPortal.Webhooks khi có connection string.
- Không đổi schema (dùng cột Status/Attempts/ProcessedAt/LastError có sẵn từ migration 011).

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `src/TAPortal.Integrations/Tingee/TingeePayments.cs`
- ADD `src/TAPortal.Integrations/Tingee/SqlTingeePaymentLedger.cs`
- MODIFY `src/TAPortal.Webhooks/Program.cs`
- ADD `tests/TACustomerPortal.Tests/SafeTingeePaymentProcessorTests.cs` (6 test)
- MODIFY `tests/TACustomerPortal.Tests/SafeCustomerPaymentsTests.cs` (FakeTingee.StatusError)

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Execution: NOT_APPLICABLE

## Test và kết quả
- Release build 0 lỗi; Safe tests 404/404 pass. SQL ledger: NOT_VERIFIED trên DB thật (Safe tests không dùng SQL) — kiểm tra bằng một giao dịch thật sau deploy.

## Publish
- Integrations đổi → webhooks (bắt buộc cho tính năng), portal, invoice. Trạng thái: xem report.

## Git
- Branch: feature/customer-portal-mvp01
- Merge: PENDING_APPROVAL

## Rủi ro, quyết định và việc còn lại
- IIS có thể dừng app pool TA.Webhooks khi rảnh: worker dừng theo, nhưng webhook tiếp theo khởi động lại site và worker xử lý cả các dòng còn chờ.
- Webhook UNMATCHED cần màn hình đối soát cho T.A (việc sau). Tự xuất hóa đơn khi InvoiceStatus=PENDING: việc sau.