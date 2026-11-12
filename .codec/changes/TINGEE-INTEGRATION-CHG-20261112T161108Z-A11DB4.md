# Change TINGEE-INTEGRATION-CHG-20261112T161108Z-A11DB4

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261112T161108Z-A11DB4`
- Created UTC: 2026-11-12T16:11:08.772116+00:00
- Tiêu đề: History page size 50 (partner refuses 200 with 1108)
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator (2026-11-12): bấm "Đồng bộ từ ngân hàng" báo "Không lấy được lịch sử giao dịch… Mã tra cứu TG26111216062679840A".

Nguyên nhân (dbo.TingeeApiLogs): mọi lời gọi transaction/get-paging với maxResultCount=200 đều bị trả 1108 "Số bản ghi của 1 trang quá lớn", dù tài liệu ghi tối đa 200. Đồng bộ chưa từng chạy được.

Đã làm: trang mặc định 50 (theo mẫu của tài liệu); gặp 1108 thì giảm một nửa và hỏi lại, thấp nhất 10; phân trang theo số bản ghi đã nhận.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TAPortal.Integrations/Tingee/{TingeeTransactionSync.cs,TingeeApi.cs}`, `tests/TACustomerPortal.Tests/SafeTingeeTransactionSyncTests.cs`

## Migration SQL (KHÔNG tự chạy)
- Không có.

## Test và kết quả
- Release build 0 lỗi; Safe tests 669/669.

## Publish
- webhooks, invoice, portal. Xem report.

## Git
- Branch: feature/tx-sync
- Commit SHA: xem report
- PR:
- Merge: xem report
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Kiểm chứng bằng lần đồng bộ tiếp theo của operator (xem dbo.TingeeApiLogs).
