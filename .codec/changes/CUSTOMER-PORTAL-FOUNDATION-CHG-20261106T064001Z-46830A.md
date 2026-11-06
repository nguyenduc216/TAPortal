# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T064001Z-46830A

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T064001Z-46830A`
- Created UTC: 2026-11-06T06:40:01.524894+00:00
- Tiêu đề: Khach hang: MST dan co khoang trang, dien thoai/Zalo tu bo khoang trang, form rong 3 cot
- Trạng thái: DEPLOYED

## Yêu cầu và phạm vi
Operator (2026-11-06), trang Thêm khách hàng: (1) mã số thuế đúng, tra cứu đã ra đơn vị nhưng vẫn báo sai định dạng; (2) nội dung bị dồn một góc, cần rộng ra; (3) số điện thoại và Zalo phải tự bỏ khoảng trắng vì khách copy từ nguồn khác.

Nguyên nhân (1): `[RegularExpression]` của TaxCode chạy lúc model binding trên giá trị thô (dán kèm dấu cách / ký tự vô hình); controller có chuẩn hóa MST nhưng không xóa lỗi đã ghi, nên MST hợp lệ vẫn bị từ chối.
Nguyên nhân (2): `.cp-form-grid` là `auto-fit minmax(220px, 1fr)` — màn hình rộng ra 6 cột trong khi mỗi nhóm chỉ 3 ô.

Sửa:
- `TaxCodeRules.Normalize` bỏ cả ký tự vô hình (zero-width, BOM); MST được kiểm tra lại sau chuẩn hóa (Revalidate).
- Điện thoại: bỏ khoảng trắng, dấu chấm, gạch ngang, ngoặc; `+84`/`84` + 9 số ⇒ `0…`; rồi mới kiểm tra 10 chữ số. Ô nhập cho phép 15 ký tự (trước 10 cắt mất số khi gõ có dấu cách).
- Zalo: nếu là số điện thoại thì lưu dạng số như trên; nếu là tên Zalo thì chỉ cắt khoảng trắng hai đầu.
- Form khách hàng dùng `.cp-form-grid--3`: 3 cột đều (2 cột dưới 1100px, 1 cột dưới 640px).

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TACustomerPortal.Web/Controllers/ContactsController.cs` (Revalidate TaxCode/Phone, PhoneDigits, Zalo)
- MODIFY `src/TACustomerPortal.Web/Services/TaxCodeLookup.cs` (Normalize bỏ ký tự vô hình)
- MODIFY `src/TACustomerPortal.Web/Views/Contacts/Edit.cshtml` (lưới 3 cột, ô điện thoại 15 ký tự)
- MODIFY `src/TACustomerPortal.Web/wwwroot/css/customer.css` (`.cp-form-grid--3`)
- MODIFY `tests/TACustomerPortal.Tests/SafeCustomerPaymentsTests.cs`

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Không có migration.
- Execution: NOT_APPLICABLE

## Test và kết quả
- `dotnet build TAPortal.sln -c Release`: 0 lỗi. Safe tests 478/478 pass (MST "4202062905 " + zero-width được lưu; điện thoại " 0366 699 999 " ⇒ 0366699999; Zalo "+84 366.699.961" ⇒ 0366699961; tên Zalo giữ nguyên; "0366 699 9" vẫn báo thiếu số).

## Publish
- invoice (TACustomerPortal.Web). Xem report.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: a5d4552 (khôi phục: 7ded836)
- PR:
- Merge: xem report
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Khách hàng đã lưu trước đây với số có khoảng trắng (nếu có) không tự đổi; sửa và lưu lại sẽ chuẩn hóa.
