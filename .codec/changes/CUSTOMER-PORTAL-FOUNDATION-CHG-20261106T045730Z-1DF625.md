# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T045730Z-1DF625

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T045730Z-1DF625`
- Created UTC: 2026-11-06T04:57:30.302372+00:00
- Tiêu đề: Checkbox hidden fallback order (auto-issue, email, package active)
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator (2026-11-06): trang Hóa đơn điện tử lưu không được "Tự động xuất hóa đơn" / "Gửi hóa đơn qua email" (đã tick nhưng sau khi lưu vẫn "Chưa tự động").

Nguyên nhân (TAPortalSys chỉ-đọc: AutoIssueEnabled=0, SendEmail=0 sau lần lưu 11:53:41): input hidden `value="false"` đặt TRƯỚC checkbox; ô được tick gửi "false","true" và model binder lấy giá trị đầu ⇒ false. Cùng lỗi: Tạo đơn hàng (AutoIssueInvoice), Cài đặt thanh toán (AutoIssueInvoiceDefault), Admin Sửa gói dịch vụ (Input.IsActive).

Sửa: đặt hidden fallback SAU checkbox ở 4 view. Không đổi dữ liệu đã lưu: doanh nghiệp tick lại và lưu.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TACustomerPortal.Web/Views/EInvoice/Index.cshtml`, `Views/Orders/Create.cshtml`, `Views/PaymentSettings/Index.cshtml`
- MODIFY `src/TAPortal.Web/Views/ServicePackages/Edit.cshtml`
- MODIFY `tests/TACustomerPortal.Tests/SafeUiDesignSystemTests.cs` (HiddenFalseFallbacksComeAfterTheirCheckboxes)

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Không có migration.
- Execution: NOT_EXECUTED

## Test và kết quả
- `dotnet build TAPortal.sln -c Release`: 0 lỗi. Safe tests 470/470 pass.

## Publish
- Project:
- Destination (chỉ từ config):
- Trạng thái: NOT_PUBLISHED
- Artifact SHA256:
- Smoke test:

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA:
- PR:
- Merge: PENDING_APPROVAL
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- 2 đơn hàng hiện có đều AutoIssueInvoice=0 (có thể do lỗi này); không sửa dữ liệu — xuất hóa đơn từ trang chi tiết đơn nếu cần.
- Thống kê đã kiểm tra: 0 gói dịch vụ trong dbo.ServicePackages.
