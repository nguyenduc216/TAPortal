# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T074510Z-F0495F

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T074510Z-F0495F`
- Created UTC: 2026-11-06T07:45:10.994455+00:00
- Tiêu đề: Danh sach don hang: cot trang thai hoa don dien tu
- Trạng thái: DEPLOYED

## Yêu cầu và phạm vi
Operator (2026-11-06): danh sách Đơn hàng & QR thêm trạng thái xuất hóa đơn để biết đơn đã xuất hay chưa.

Sửa: cột "Hóa đơn" sau "Trạng thái": badge Đã xuất (xanh, kèm số hóa đơn) / Đang xuất (vàng) / Xuất hóa đơn lỗi (đỏ, lý do đã lọc tên đối tác hiện khi rê chuột) / Chưa xuất (kèm "Tự xuất khi thanh toán đủ" hoặc "Xuất tay"). Dùng lại `InvoiceText.Status`.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TACustomerPortal.Web/Views/Orders/Index.cshtml`
- MODIFY `tests/TACustomerPortal.Tests/SafeCustomerPaymentsTests.cs` (TheOrderListShowsTheInvoiceStatus)

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Không có migration.
- Execution: NOT_APPLICABLE

## Test và kết quả
- Release build 0 lỗi; Safe tests 480/480.

## Publish
- invoice (TACustomerPortal.Web). Xem report.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: 1329373
- PR:
- Merge: xem report
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Không có.
