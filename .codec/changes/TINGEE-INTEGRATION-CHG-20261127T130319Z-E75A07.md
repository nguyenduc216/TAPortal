# Change TINGEE-INTEGRATION-CHG-20261127T130319Z-E75A07

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261127T130319Z-E75A07`
- Created UTC: 2026-11-27T13:03:19.988270+00:00
- Tiêu đề: Payment collections: one QR for several unpaid orders, allocated oldest first
- Trạng thái: DEPLOYED

## Yêu cầu và phạm vi
Operator (2026-11-27): khách có hai đơn đang nợ cần gộp vào một QR công nợ chuẩn, linh hoạt: khách có thể chỉ chuyển trước một khoản. Đã duyệt hướng "Thu gộp công nợ" (phiếu thu gộp đứng trên các đơn cũ, không tạo đơn mới), sau đó "Tôi đồng ý, hãy thực hiện theo cái định hướng này nhé."

Quyết định:
- **Không gộp thành đơn mới.** Một phiếu thu (TT-yyMMdd-NNNN) có QR riêng, link riêng (/pay/c/{token}), số tiền do người bán chọn. Mặc định là tổng còn nợ của các đơn đã chọn, có thể nhỏ hơn.
- **Một đơn chỉ nằm trong một phiếu thu đang mở.** Kiểm tra lại trong transaction khi tạo.
- **Phân bổ tiền:** tiền về phiếu thu được tính hết vào phiếu, rồi chia cho các đơn theo thứ tự (mặc định đơn cũ trước, người dùng đổi bằng ↑ ↓). Mỗi đơn nhận tối đa số còn nợ.
  - Đơn đã hủy, đã thu đủ hoặc EXPIRED_PAID không nhận tiền.
  - Đơn đủ tiền thì `InvoiceStatus` chuyển PENDING, xuất hóa đơn riêng như luồng cũ.
- **Trạng thái phiếu:** PAID khi đạt số tiền hoặc không còn đơn nào nợ, ngược lại là PARTIALLY_PAID.
- **REVIEW thay vì MATCHED** khi:
  - trả dư (OVERPAID),
  - phiếu đã hủy (COLLECTION_CANCELLED, tiền vẫn chia vào nợ),
  - QR hết hạn,
  - lệch số liệu với đối tác.
- **Thứ tự khớp:** bộ xử lý tìm phiếu thu trước (billId hoặc mã TT trong nội dung chuyển khoản), không thấy thì khớp đơn như cũ.
- **Giao dịch:** một dòng PaymentTransactions có CollectionId (OrderId NULL); cách chia nằm ở PaymentAllocations. Đối soát hiện mã phiếu thu. "Chấp nhận" một giao dịch REVIEW của phiếu thu chỉ đóng việc xem lại.
- **Thông báo:** "Phiếu thu TT… đã nhận …" (Zalo/email/Telegram dùng chung mẫu, `{order_no}` là mã phiếu thu).
- **QR:** động nếu ngân hàng hỗ trợ, tĩnh nếu không. Kiểm tra QR còn sống và tạo lại như đơn. Tạo lại QR cho số còn phải thu.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `database/migrations/069-payment-collections.sql`, `database/scripts/125-verify-payment-collections.sql`, `database/scripts/126-rollback-payment-collections.sql`
- ADD `src/TAPortal.Integrations/Tingee/PaymentCollections.cs` (quy tắc phân bổ, khớp phiếu thu)
- MODIFY `src/TAPortal.Integrations/Tingee/TingeePayments.cs` (ledger interface, processor, cờ COLLECTION_CANCELLED)
- MODIFY `src/TAPortal.Integrations/Tingee/SqlTingeePaymentLedger.cs` (ứng viên phiếu thu, ghi phân bổ trong một transaction)
- MODIFY `src/TAPortal.Integrations/Tingee/PaymentTransactions.cs` (đối soát hiện phiếu thu, Accept)
- MODIFY `src/TAPortal.Integrations/Notifications/Notifications.cs`, `SqlNotificationStore.cs`
- ADD `src/TACustomerPortal.Web/Services/CollectionStore.cs`, `CollectionPayments.cs`, `Models/CollectionModels.cs`
- ADD `src/TACustomerPortal.Web/Controllers/CollectionsController.cs`, `CollectionPayController.cs`
- ADD `src/TACustomerPortal.Web/Views/Collections/{Index,Create,Details}.cshtml`, `Views/Pay/Collection.cshtml`, `Views/Pay/_PayScripts.cshtml` (script tách từ Pay/Index)
- MODIFY `src/TACustomerPortal.Web/Program.cs`, `Views/Shared/_Layout.cshtml` (menu "Thu gộp công nợ"), `Views/Contacts/Index.cshtml` (nút "Thu công nợ"), `Controllers/OrdersController.cs`, `Models/PaymentModels.cs`, `Views/Orders/Details.cshtml` (ghi chú đơn đang trong phiếu thu), `Views/Pay/Index.cshtml`, `Views/Transactions/Index.cshtml`, `wwwroot/css/customer.css`, `wwwroot/js/customer.js`
- MODIFY `src/TAPortal.Web/Views/PaymentReconciliation/Index.cshtml`
- ADD `tests/TACustomerPortal.Tests/SafePaymentCollectionTests.cs`; MODIFY `SafeCustomerPaymentsTests.cs` (store bộ nhớ), `SafeReleaseRegressionTests.cs` (3 endpoint công khai mới đã review)

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script: `database/migrations/069-payment-collections.sql`, sau 068
- Preflight / Verify / Rollback: `125-verify-payment-collections.sql` / `126-rollback-payment-collections.sql` (từ chối khi đã có phiếu thu)
- Chỉ thêm 3 bảng và 1 cột nullable, không đổi dòng nào.
- Execution: EXECUTED 2026-11-27 13:24:40Z qua `Invoke-TAMigrations.ps1 -Plan "069:125:126"` tại HEAD 3ad557a.
  - Pre-check: NOT_APPLIED.
  - Dry-run: afterMigration=PASS, afterRollback=NOT_APPLIED, trancount 0. Pre-check lại: NOT_APPLIED.
  - Backup COPY_ONLY đã kiểm tra: `TAPortalSys-predeploy-20261127T132303Z.bak`.
  - Gates: 867/867 PASS.
  - Real run: VERIFY_STATUS=PASS.
  - Log: `.codec/reports/TINGEE-INTEGRATION-CHG-20261127T130319Z-E75A07-migrations-20261127T132303Z.log`.

## Test và kết quả
- `dotnet build TAPortal.sln -c Release`: 0 lỗi.
- Safe tests: 867/867 PASS (6 test mới SafePaymentCollectionTests).
- Codec unittest OK; codec validate: chỉ các lỗi "Publish path violates fixed convention" đã biết của checkout này.

## Publish
- Project: TAPortal.Web, TACustomerPortal.Web, TAPortal.Webhooks (Integrations đổi)
- Trạng thái: DEPLOYED 2026-11-27 13:27-13:28Z, source commit 0f21276, bằng `Deploy-TARemote.ps1 -Target portal,invoice,webhooks -MigrationsApplied`.

| Target | Artifact SHA256 | Package SHA256 | Health |
|---|---|---|---|
| portal | 5c07aa1a5d3ce915bf0f232a576da83d82dcf49e6a69f8b04e1ab7551c22b096 | 919254BD93CA5255895DEDF71246BCDDDAEA19CBF17FFBE2C86D1A2004D80E97 | HTTP 200 |
| invoice | 18626f2b6fcc176d83b358aa9af66a536f18b5e647dba8a1aaf0c3ad6fc8a06a | 922F5F87B7ED5B7BE0F7B902C201316BFC8EAE47FD7DA11EBFAF8A0034EB803E | HTTP 200 |
| webhooks | 6eca21a0fece997ab5049450262174e24ddb00f300deffbe77b73fd1c1cc4c0e | 6342C1EA24AC1DDF0E14E1BD15F97F4D1FD21569204E6C683389780096594EDD | HTTP 200 |

- Backup trên server: `C:TADeployackupsportal-20261127-202732`, `C:TADeployackupsinvoice-20261127-202741`, `C:TADeployackupswebhooks-20261127-202749`.
- Smoke test: `/pay/c/<token không tồn tại>` trả 404 (đọc được bảng mới, không phải 503); `/collections` trả 302 về đăng nhập.

## Git
- Branch: feature/payment-collections (cắt từ main b08c91f)
- Merge: MERGED fast-forward vào main tại 393053d (sau deploy)

## Rủi ro, quyết định và việc còn lại
- Chưa có kênh tự gửi link qua Zalo khi tạo phiếu thu: người bán sao chép link gửi khách, giống link đơn.
- QR tạo lại khi một số đơn đã trả riêng: số đối chiếu với đối tác có thể lệch (cờ "Lệch đối soát", không chặn).
- Phiếu thu chỉ dùng cho đơn có lưu khách hàng (không áp dụng khách lẻ).
