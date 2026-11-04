# Change TINGEE-INTEGRATION-CHG-20261104T182406Z-ECDCF6

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261104T182406Z-ECDCF6`
- Created UTC: 2026-11-04T18:24:06+00:00
- Tiêu đề: Customer Portal: automatic customer codes (KH001, KH002...)
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator (2026-11-05): "Mã khách hàng nên tự động" (Khách hàng → Thêm khách hàng).

- Form thêm khách hàng điền sẵn mã kế tiếp: `KH` + (số lớn nhất trong các mã dạng `KH<số>` của doanh nghiệp, kể cả khách đã ngừng) + 1, tối thiểu 3 chữ số (KH001, KH002…; KH0999 → KH1000). Mã khác dạng (VIP01…) không ảnh hưởng.
- Vẫn sửa được; để trống thì server tự lấy mã kế tiếp. Nếu mã đề xuất vừa bị người khác dùng: báo lỗi và đề xuất mã mới kế tiếp.
- Sửa khách hàng: không đổi, mã vẫn bắt buộc. Không có thay đổi SQL (uniqueness vẫn do store/DB).

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TACustomerPortal.Web/Controllers/ContactsController.cs`
- MODIFY `src/TACustomerPortal.Web/Views/Contacts/Edit.cshtml`
- MODIFY `tests/TACustomerPortal.Tests/SafeCustomerPaymentsTests.cs` (test `NewCustomerCodesAreNumberedAutomatically`)

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Execution: NOT_APPLICABLE

## Test và kết quả
- `dotnet build TAPortal.sln -c Release`: 0 lỗi. Safe tests 396/396 pass (worktree deploy sạch, không gồm file chưa commit của agent khác).

## Publish
- Project: TACustomerPortal.Web (invoice). Trạng thái: xem report.

## Git
- Branch: feature/customer-portal-mvp01
- Merge: PENDING_APPROVAL

## Rủi ro, quyết định và việc còn lại
- Không dùng sequence trong DB: hai người thêm cùng lúc có thể trùng mã đề xuất; người lưu sau nhận lỗi và mã mới đề xuất (không mất dữ liệu).
