# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T071511Z-E55D21

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T071511Z-E55D21`
- Created UTC: 2026-11-06T07:15:11.121497+00:00
- Tiêu đề: Trang QR: khach hang ben trai, nguoi ban ben phai, icon ngan hang o giua
- Trạng thái: DEPLOYED

## Yêu cầu và phạm vi
Operator (2026-11-06): trang thanh toán QR công khai (/pay/{token}) — đổi vị trí hai bên và thay icon giữa bằng icon ngân hàng.

Sửa: khách hàng bên trái, người bán bên phải (đúng chiều dòng tiền: khách → ngân hàng → người bán); icon giữa `building-bank` thay `link`; vòng logo theo vai (`cp-pay__party--buyer` xanh, `--seller` vàng) thay cho `:last-child`; đường nối trái xanh, phải vàng, nút giữa chuyển màu xanh → vàng. Đơn không có khách: chỉ hiện người bán như trước.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TACustomerPortal.Web/Views/Pay/Index.cshtml`
- MODIFY `src/TACustomerPortal.Web/wwwroot/css/customer.css`
- MODIFY `tests/TACustomerPortal.Tests/SafeCustomerPayPageTests.cs`

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Không có migration.
- Execution: NOT_APPLICABLE

## Test và kết quả
- Release build 0 lỗi; Safe tests 478/478 (thứ tự khách → cầu nối → người bán, icon ngân hàng ở giữa). Xem giao diện bằng trang thử dùng customer.css thật.

## Publish
- invoice (TACustomerPortal.Web). Xem report.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: fdbc25d
- PR:
- Merge: xem report
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Không có.
