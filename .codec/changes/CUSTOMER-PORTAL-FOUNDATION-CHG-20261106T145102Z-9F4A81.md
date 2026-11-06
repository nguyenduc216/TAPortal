# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T145102Z-9F4A81

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T145102Z-9F4A81`
- Created UTC: 2026-11-06T14:51:02.125407+00:00
- Tiêu đề: Customer Portal logo: clean vector redraw, favicon and link-preview image
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator (2026-11-06): thay logo của Customer Portal (hoadontudong) bằng ảnh operator gửi; ảnh gốc nét không thẳng, lem ⇒ vẽ lại sạch trước khi thay.

Sửa:
- Vẽ lại logo thành vector `wwwroot/images/logo.svg` (3 màu xanh navy #0B2E50, xanh lá #034F00, vàng #F4BA00): dải navy hình chữ S có đoạn chéo thẳng 45°, cạnh trên/dưới thẳng, đường cong mượt; khe trắng giữa xanh lá và dải navy đều nhau (mask theo viền dải), mảng vàng vẽ cạnh rõ ràng. Đã so chồng lên ảnh gốc để giữ đúng hình.
- Header sidebar dùng `logo.svg` (thay `ta-logo.png`, file này bỏ khỏi Customer Portal; Admin portal giữ logo T.A).
- `favicon-32x32.png` và ảnh xem trước khi chia sẻ link `og-share.png` (1200×630, nền trắng, logo giữa, vạch xanh lá dưới) dựng lại từ logo mới.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `src/TACustomerPortal.Web/wwwroot/images/logo.svg`
- DELETE `src/TACustomerPortal.Web/wwwroot/images/ta-logo.png`
- MODIFY `src/TACustomerPortal.Web/wwwroot/images/favicon-32x32.png`, `og-share.png`
- MODIFY `src/TACustomerPortal.Web/Views/Shared/_Layout.cshtml`, `Views/Pay/Index.cshtml` (chú thích)
- MODIFY `tests/TACustomerPortal.Tests/SafeUiDesignSystemTests.cs` (TheCustomerPortalShowsItsVectorLogo)

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Không có migration.
- Execution: NOT_APPLICABLE

## Test và kết quả
- Release build 0 lỗi; Safe tests 481/481.
- Ảnh logo, favicon và og-share xem trực tiếp sau khi dựng bằng Chrome headless.

## Publish
- invoice (TACustomerPortal.Web). Xem report.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: xem report
- PR:
- Merge: xem report
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Zalo/Facebook lưu đệm ảnh xem trước: link đã chia sẻ trước đây có thể vẫn hiện ảnh cũ một thời gian.
- Trình duyệt lưu đệm favicon: có thể cần tải lại trang.
