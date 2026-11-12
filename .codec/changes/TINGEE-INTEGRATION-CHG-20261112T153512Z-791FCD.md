# Change TINGEE-INTEGRATION-CHG-20261112T153512Z-791FCD

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261112T153512Z-791FCD`
- Created UTC: 2026-11-12T15:35:12.047501+00:00
- Tiêu đề: Sync also reads root-account payments; one sync run at a time
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator (2026-11-12): bộ phận hỗ trợ của đối tác cho biết giao dịch chuyển thẳng vào số tài khoản gốc đã liên kết vẫn rà soát được; bấm "Đồng bộ từ ngân hàng" thì báo "Vừa đồng bộ xong, vui lòng thử lại sau ít giây".

Nguyên nhân:
- Đồng bộ chỉ hỏi lịch sử theo danh sách VA và chỉ giữ giao dịch trên VA, nên tiền vào số gốc MB (không có VA) bị bỏ qua.
- Giới hạn 20 giây tính từ lúc bắt đầu, nên bấm lần hai khi lần đầu vừa xong vẫn bị từ chối với thông báo khó hiểu.

Đã làm:
- `TingeeTransactionSync`: mỗi merchant đọc thêm toàn bộ lịch sử (không lọc VA; lỗi thì ghi log và tiếp tục theo VA như cũ), giữ giao dịch có VA hoặc số tài khoản gốc thuộc các tài khoản đã liên kết (`Accounts`, `Belongs`).
- Mỗi doanh nghiệp chỉ một lượt đồng bộ chạy cùng lúc ("Đang đồng bộ, vui lòng chờ…"); khoảng nghỉ 5 giây tính từ lúc lượt trước kết thúc.
- `SqlTingeePaymentLedger.CandidatesAsync`: tiền vào số gốc khớp đơn của tài khoản đó, không còn cần cờ thông báo số gốc.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TAPortal.Integrations/Tingee/{TingeeTransactionSync.cs,SqlTingeePaymentLedger.cs}`, `tests/TACustomerPortal.Tests/SafeTingeeTransactionSyncTests.cs`

## Migration SQL (KHÔNG tự chạy)
- Không có.

## Test và kết quả
- Release build 0 lỗi; Safe tests 669/669 (mới: RootAccountPaymentsBelongToTheSyncAndMerchantWideQueriesSendNoVaFilter).

## Publish
- webhooks, invoice, portal. Xem report.

## Git
- Branch: feature/tx-sync
- Commit SHA: xem report
- PR:
- Merge: xem report
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Chưa xác minh được đối tác trả giao dịch số gốc trong lịch sử cho đến khi operator bấm đồng bộ trên trang thật.
