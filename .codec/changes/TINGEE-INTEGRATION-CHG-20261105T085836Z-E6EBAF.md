# Change TINGEE-INTEGRATION-CHG-20261105T085836Z-E6EBAF

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261105T085836Z-E6EBAF`
- Created UTC: 2026-11-05T08:58:37Z
- Tiêu đề: Customer Portal sidebar in the T.A Admin style; remove the "Cần hỗ trợ?" box
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator (2026-11-05): "menu bên hệ thống hoá đơn làm style giống trang admin, bỏ phần cần hỗ trợ đi".
- Sidebar của TACustomerPortal.Web dùng bảng màu của Admin (nền navy #082746, chữ #C6D4E5, nhãn nhóm in hoa màu #8FA6C1, mục đang chọn nền trắng mờ + vạch vàng #E5A900 bên trái + icon vàng), logo T.A như Admin. Giữ nguyên các mục menu, ô chọn doanh nghiệp, nút Đăng xuất, chế độ thu gọn và drawer di động.
- Bỏ ô "Cần hỗ trợ?".

## File thay đổi
- MODIFY `src/TACustomerPortal.Web/Views/Shared/_Layout.cshtml`, `src/TACustomerPortal.Web/wwwroot/css/customer.css`
- ADD `src/TACustomerPortal.Web/wwwroot/images/ta-logo.png` (bản sao logo của Admin)
- MODIFY `tests/TACustomerPortal.Tests/SafeUiDesignSystemTests.cs`

## Migration SQL
- NOT_APPLICABLE.

## Publish
- Project: TACustomerPortal.Web (target `invoice`). Xem report.

## Git
- Branch: feature/customer-portal-mvp01
