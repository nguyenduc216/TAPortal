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

## Test và kết quả
- Release build 0 lỗi; Safe tests 416/416 pass; Codec unit tests OK; `codec validate` chỉ còn lỗi đường dẫn publish có sẵn (vị trí worktree).

## Migration SQL
- NOT_APPLICABLE.

## Publish
- Project: TACustomerPortal.Web (target `invoice`): DEPLOYED 2026-11-05T09:01:56Z, source 7c3eac0, artifact SHA256 3877dfc9c51d0a2f98d3835638e00c60ed641da3d40fa9ce323048d2d5e1bdac, package SHA256 6C6111D5C34482E91E1CB11A0188684DA1F581798A79A2BC5D074075626304E1, health HTTP 200, server backup invoice-20261105-160150 (kept alone).

## Git
- Branch: feature/customer-portal-mvp01; merged to main (fast-forward) after this record.
