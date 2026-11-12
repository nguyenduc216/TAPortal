# Change TINGEE-INTEGRATION-CHG-20261112T103925Z-D8A330

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261112T103925Z-D8A330`
- Created UTC: 2026-11-12T10:39:25.892952+00:00
- Tiêu đề: Đồng bộ giao dịch thiếu IPN từ lịch sử Tingee; tổng tiền vào/ra trên đối soát
- Trạng thái: DEPLOYED

## Yêu cầu và phạm vi
Operator (2026-11-12): hôm nay có giao dịch 5,4 triệu vào tài khoản (không qua QR) nhưng trang tra soát không thấy; cần biết mỗi ngày tiền vào, tiền ra bao nhiêu; khi IPN không bắn về thì cần nút đồng bộ lấy lại các giao dịch chưa có IPN; kiểm tra API Tingee có đủ chức năng.

Kiểm tra: DB production (chỉ đọc) — IPN Payment cuối cùng về dbo.TingeeWebhookInbox ngày 06/11; không có IPN nào cho giao dịch 5,4 triệu. Tingee có API `POST /v1/transaction/get-paging` (lịch sử giao dịch ghi có/ghi nợ theo merchant, VA, khoảng thời gian tối đa 10 ngày, 200 bản ghi/trang; trường `code` là mã giao dịch ngân hàng = `transactionCode` của webhook).

Đã làm:
- `TingeeApi.GetTransactionsAsync` (transaction/get-paging).
- `TingeeTransactionSync`: lấy lịch sử theo tài khoản nhận tiền (VA) của khách hàng (hoặc tất cả), chia khoảng 9 ngày, phân trang; giao dịch chưa có trong inbox/PaymentTransactions được đưa vào dbo.TingeeWebhookInbox đúng định dạng webhook (cùng DedupKey `tx:<mã>`, RequestId `sync:<transactionId>`, ReceivedAt = giờ giao dịch), bộ xử lý ở site webhook khớp đơn như IPN thường; IPN đến sau bị coi là trùng. Chờ tối đa 8 giây cho bộ xử lý. Tối đa 31 ngày/lần, cách nhau tối thiểu 20 giây.
- Hóa đơn tự động → Đối soát giao dịch: thẻ "Tiền vào"/"Tiền ra" theo khoảng lọc, liên kết "Xem hôm nay", nút "Đồng bộ từ ngân hàng" (OWNER/ADMIN), nhãn "Lấy lại qua đồng bộ".
- TA Portal → Đối soát giao dịch: thẻ Tiền vào/Tiền ra, nút "Đồng bộ từ Tingee" (theo khách hàng đang lọc hoặc tất cả, quyền ReconciliationManage), nhãn "Không có IPN · đã đồng bộ".

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `src/TAPortal.Integrations/Tingee/TingeeTransactionSync.cs`, `tests/TACustomerPortal.Tests/SafeTingeeTransactionSyncTests.cs`
- MODIFY `src/TAPortal.Integrations/{IntegrationsServiceCollectionExtensions.cs,Tingee/TingeeApi.cs,Tingee/PaymentTransactions.cs}`, `src/TACustomerPortal.Web/{Controllers/TransactionsController.cs,Views/Transactions/Index.cshtml,wwwroot/css/customer.css}`, `src/TAPortal.Web/{Controllers/PaymentReconciliationController.cs,Views/PaymentReconciliation/Index.cshtml}`

## Migration SQL (KHÔNG tự chạy)
- Không có.

## Test và kết quả
- Release build 0 lỗi; Safe tests 667/667 (mới: SafeTingeeTransactionSyncTests).

## Publish
- invoice và portal. Xem report.

## Git
- Branch: feature/tx-sync
- Commit SHA: 33019c5
- PR:
- Merge: fast-forward main
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Nguyên nhân thiếu IPN cần đối chiếu log IIS site webhook và cấu hình webhook phía Tingee.
