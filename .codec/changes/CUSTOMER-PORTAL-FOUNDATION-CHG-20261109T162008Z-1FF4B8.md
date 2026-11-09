# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261109T162008Z-1FF4B8

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261109T162008Z-1FF4B8`
- Created UTC: 2026-11-09T16:20:08.055781+00:00
- Tiêu đề: San pham khach hang va gia rieng theo khach
- Trạng thái: DEPLOYED

## Yêu cầu và phạm vi
Operator (2026-11-09): quản lý sản phẩm của khách hàng trên Hóa đơn tự động. Sản phẩm có tên, hình ảnh, đơn giá (đã gồm VAT), trạng thái sử dụng; tạo đơn hàng thì chọn khách rồi chọn sản phẩm. Đơn giá danh mục chỉ là giá tham khảo: khi lưu đơn có sản phẩm bị đổi giá so với giá đang dùng, hệ thống hỏi có lưu giá này cho khách không; đồng ý thì lần sau chọn sản phẩm đó cho khách này tự lấy giá đã lưu, không thì dùng giá mặc định.
Đã làm:
- Menu "Sản phẩm" (Thanh toán): danh sách (ảnh, tên, ĐVT, đơn giá gồm VAT, thuế suất, số khách có giá riêng, trạng thái), thêm/sửa (ảnh PNG/JPG/WEBP ≤ 1 MB lưu trong DB), ngừng/mở bán; trang sửa liệt kê giá riêng theo khách và cho bỏ giá riêng. Chỉ OWNER/ADMIN được sửa.
- Tạo đơn: nút "Chọn sản phẩm" mở lưới sản phẩm đang bán (ảnh, tên, giá; nhãn "Giá riêng" nếu khách đã có giá), bấm để thêm dòng hoặc tăng số lượng; đổi khách thì dòng sản phẩm còn ở giá cũ chuyển sang giá của khách mới. Cột đơn giá của form là giá gồm VAT (PricesIncludeTax): khách trả đúng số lượng × đơn giá; đơn giá trước thuế gửi hóa đơn = làm tròn đơn giá/(1+thuế suất).
- Lưu đơn: dòng sản phẩm có giá khác giá đang dùng của khách → hộp "Lưu giá cho khách hàng này?" (tích chọn từng sản phẩm). Giá lưu bằng giá danh mục thì xóa giá riêng. Khách lẻ không hỏi.
- Dòng đơn hàng lưu ProductId (chỉ nhận sản phẩm cùng công ty).

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `database/migrations/034-customer-products.sql`, `database/scripts/057-verify-customer-products.sql`, `database/scripts/058-rollback-customer-products.sql`
- ADD `src/TACustomerPortal.Web/Controllers/ProductsController.cs`, `src/TACustomerPortal.Web/Models/ProductModels.cs`, `src/TACustomerPortal.Web/Services/ProductStore.cs`, `src/TACustomerPortal.Web/Views/Products/Index.cshtml`, `src/TACustomerPortal.Web/Views/Products/Edit.cshtml`, `tests/TACustomerPortal.Tests/SafeCustomerProductsTests.cs`
- MODIFY `src/TACustomerPortal.Web/Controllers/OrdersController.cs`, `src/TACustomerPortal.Web/Models/PaymentModels.cs`, `src/TACustomerPortal.Web/Program.cs`, `src/TACustomerPortal.Web/Services/OrderPayments.cs`, `src/TACustomerPortal.Web/Services/PaymentStore.cs`, `src/TACustomerPortal.Web/Views/Orders/Create.cshtml`, `src/TACustomerPortal.Web/Views/Shared/_Layout.cshtml`, `src/TACustomerPortal.Web/wwwroot/css/customer.css`, `src/TACustomerPortal.Web/wwwroot/js/customer.js`, `tests/TACustomerPortal.Tests/SafeCustomerPaymentsTests.cs`

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path: database/migrations/034-customer-products.sql
- Thứ tự chạy: sau 033
- Preflight / Verify / Rollback: database/scripts/057-verify-customer-products.sql / database/scripts/058-rollback-customer-products.sql
- Execution: EXECUTED 2026-11-09 16:23Z (dry-run có rollback PASS, backup COPY_ONLY đã verify)

## Test và kết quả
- Release build 0 lỗi; Safe tests 622/622 (14 test mới SafeCustomerProductsTests); trang thử trên trình duyệt: chọn sản phẩm, tổng tiền gồm VAT đúng (2 × 35.000 = 70.000), hộp hỏi lưu giá gửi RememberPrice đúng dòng, giao diện điện thoại 2 cột không tràn ngang.
- `codec.py validate` báo lỗi có sẵn từ trước, không liên quan thay đổi này: "Publish path violates fixed convention: TAPortal.Webhooks".

## Publish
- invoice. Xem report.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: 7fbdc2c
- PR:
- Merge: fast-forward main
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Quyết định: form tạo đơn nhập đơn giá gồm VAT cho mọi dòng (kể cả dòng nhập tay), đúng với danh mục; request không gửi PricesIncludeTax vẫn hiểu là giá trước thuế như cũ.
- Hóa đơn điện tử nhận đơn giá trước thuế là số nguyên: nhà cung cấp hóa đơn tự tính lại có thể lệch 1–2 đồng so với số khách trả khi số lượng > 1 (ví dụ 2 × 35.000 gồm 8%).
- Rollback 058 từ chối khi đã có sản phẩm (dữ liệu khách).
