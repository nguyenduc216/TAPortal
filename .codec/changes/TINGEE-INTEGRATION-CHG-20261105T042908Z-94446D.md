# Change TINGEE-INTEGRATION-CHG-20261105T042908Z-94446D

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261105T042908Z-94446D`
- Created UTC: 2026-11-05T04:29:08.391839+00:00
- Tiêu đề: Payment reconciliation: order-number matching, system vs partner amounts, EXPIRED_PAID/CANCELLED_PAID, overpaid, reconciliation pages, order summary cards
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Quyết định của operator (2026-11-05), áp lên bộ xử lý webhook của change CEA3FA (chưa deploy):
1. Chỉ ghép theo mã đơn (billId của QR động hoặc mã đơn trong nội dung CK). Bỏ cách ghép theo số tiền; VietinBank không có nội dung ⇒ chưa xử lý.
2. Kết hợp hai nguồn: hệ thống tự tính (đã thu trước + số tiền giao dịch) và số Tingee báo (QR động). Lưu cả hai trên giao dịch (`SystemPaidBefore/After`, `PartnerPaidTotal`) và trên đơn (`PaidAmount`, `PartnerPaidAmount/PartnerCheckedAt`). Lệch ⇒ cờ `PARTNER_MISMATCH`. "Kiểm tra thanh toán" chỉ lưu số đối tác, không cộng tiền (không còn khả năng cộng trùng).
3. Giao dịch không tìm được đơn, body không đọc được, số tiền không hợp lệ, hoặc lỗi xử lý sau 10 lần ⇒ luôn ghi `UNMATCHED` (chưa xử lý). Tiền ra (debit) ⇒ `IGNORED` (chỉ lưu lại).
4. Màn hình đối soát: Admin T.A (`/PaymentReconciliation`, mọi khách hàng, lọc theo khách hàng / chưa gắn khách hàng / trạng thái / ngày / từ khóa) và Customer Portal (`/Transactions`, chỉ giao dịch của doanh nghiệp đang đăng nhập; thao tác cần OWNER/ADMIN). Thao tác: gán vào đơn (UNMATCHED), chấp nhận cho đơn (REVIEW), đánh dấu đã xử lý kèm lý do.
5. Trả dư ⇒ cờ `OVERPAID`, giao dịch `REVIEW`.
6. Tiền vào đơn đã hủy ⇒ đơn `CANCELLED_PAID` (Đơn hủy có giao dịch).
7. Tiền vào sau khi QR hết hạn ⇒ đơn `EXPIRED_PAID` (QR hết hạn có giao dịch).
9. AGENTS.md: thêm `webhooks` vào quyền deploy thường trực.
10. Danh sách đơn hàng: thẻ tổng hợp theo trạng thái (số đơn, tổng tiền, đã thu), thêm thẻ Trả dư và Lệch đối soát; bấm thẻ để lọc.
(Mục 8 — kênh thông báo Zalo OA/Telegram/SMTP — là change riêng.)

Audit lỗi của CEA3FA (chưa deploy): HIGH — `CK_TWI_Status` (migration 011) không có `UNMATCHED`, nên đánh dấu giao dịch không khớp sẽ lỗi CHECK và giao dịch bị thử lại mãi rồi FAILED. Sửa bằng migration 019. MEDIUM — `UpdateOrderAsync` của portal ghi đè `PaidAmount` bằng giá trị đã đọc trước khi gọi Tingee (mất tiền do worker vừa cộng). Sửa: portal không bao giờ ghi `PaidAmount`; trạng thái thanh toán tính lại từ số đã lưu; không hủy đơn đã có tiền; không đè `EXPIRED_PAID/CANCELLED_PAID`.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `AGENTS.md` (webhooks trong quyền deploy thường trực)
- ADD `database/migrations/019-payment-reconciliation.sql`
- ADD `database/scripts/027-verify-payment-reconciliation.sql`
- ADD `database/scripts/028-rollback-payment-reconciliation.sql`
- MODIFY `src/TAPortal.Integrations/Tingee/TingeePayments.cs` (matcher, `TingeePaymentRules`, ledger interface, processor)
- MODIFY `src/TAPortal.Integrations/Tingee/SqlTingeePaymentLedger.cs`
- ADD `src/TAPortal.Integrations/Tingee/PaymentTransactions.cs` (store đối soát dùng chung, nhãn tiếng Việt)
- MODIFY `src/TACustomerPortal.Web/Services/PaymentStore.cs`, `Services/OrderPayments.cs`, `Models/PaymentModels.cs`, `Controllers/OrdersController.cs`, `Program.cs`
- ADD `src/TACustomerPortal.Web/Controllers/TransactionsController.cs`, `Views/Transactions/Index.cshtml`
- MODIFY `src/TACustomerPortal.Web/Views/Orders/Index.cshtml`, `Views/Orders/Details.cshtml`, `Views/Shared/_Layout.cshtml`, `wwwroot/css/customer.css`
- ADD `src/TAPortal.Web/Controllers/PaymentReconciliationController.cs`, `Views/PaymentReconciliation/Index.cshtml`
- MODIFY `src/TAPortal.Web/Program.cs`, `Views/Shared/_Layout.cshtml`
- MODIFY `tests/TACustomerPortal.Tests/SafeTingeePaymentProcessorTests.cs`, `SafeCustomerPaymentsTests.cs`, `SafeCustomerWorkspaceTests.cs`

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path: `database/migrations/019-payment-reconciliation.sql`
- Thứ tự chạy: sau 018. Chỉ thêm cột/bảng và nới 2 CHECK constraint; không sửa/xóa dòng nào.
- Preflight / Verify / Rollback: `database/scripts/027-verify-payment-reconciliation.sql` / `database/scripts/028-rollback-payment-reconciliation.sql` (từ chối rollback khi đã có giao dịch hoặc trạng thái mới).
- Execution: EXECUTED 2026-11-05T05:03:24Z via `deploy/windows/Invoke-TAMigrations.ps1` (sqlcmd -E on ta-server): pre-check NOT_APPLIED → dry-run in one transaction (migration → verify PASS → rollback → verify NOT_APPLIED → ROLLBACK, @@TRANCOUNT=0) → pre-check again NOT_APPLIED → backup: latest full 1103 min old (< 24 h) → gates restore/build/Safe 416/416 PASS → real run → verify PASS. Log: `.codec/reports/TINGEE-INTEGRATION-CHG-20261105T042908Z-94446D-migrations-20261105T050148Z.log`.

## Test và kết quả
- Release build 0 lỗi; Safe tests 410/410 pass (thêm: quy tắc trạng thái, đối soát hai nguồn, đối tác không truy cập được, lỗi xử lý ⇒ UNMATCHED, trang đối soát chỉ thấy giao dịch của doanh nghiệp, MEMBER không xử lý được, thẻ tổng hợp, trang Admin thấy giao dịch chưa gắn khách hàng).
- SQL (ledger, store đối soát): NOT_VERIFIED trên DB thật — kiểm tra bằng giao dịch thật sau deploy.

## Publish
- Project: TAPortal.Webhooks, TAPortal.Web, TACustomerPortal.Web (Integrations đổi).
- Trạng thái: xem report.

## Git
- Branch: feature/customer-portal-mvp01
- Merge: PENDING_APPROVAL

## Rủi ro, quyết định và việc còn lại
- Chỉ ghép theo mã đơn: khách gõ sai/xóa nội dung CK ⇒ chưa xử lý, nhân viên gán tay.
- QR hết hạn: giao dịch đến sau `ExpiresAt` ⇒ `EXPIRED_PAID` kể cả khi chỉ trễ vài phút (đúng quyết định); nhân viên "Chấp nhận cho đơn" để chuyển về trạng thái theo số đã thu.
- Lệch đối soát khi Tingee báo nhiều hơn hệ thống (thiếu webhook): đơn hiện "Lệch đối soát"; không tự tạo giao dịch để tránh cộng trùng khi webhook đến muộn.
- Gửi thông báo (Zalo OA/Telegram/SMTP): change riêng; cột `NotifiedAt` đã có sẵn.
- 2026-11-05: operator cấp quyền đọc DB production ⇒ ADD `deploy/windows/Invoke-TAReadQuery.ps1` (chặn từ khóa ghi/DDL/EXEC, chạy trong transaction luôn ROLLBACK, lock timeout 5 giây), MODIFY `AGENTS.md`. Kiểm tra sau deploy: inbox 5 dòng đều `BankLink` RECEIVED (chưa có webhook thanh toán), PaymentTransactions 0, NotificationChannels 0, NotificationOutbox 0, đơn: 1 AWAITING_PAYMENT, 1 CANCELLED.
