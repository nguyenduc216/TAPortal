# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261107T004158Z-0D7E07

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261107T004158Z-0D7E07`
- Created UTC: 2026-11-07T00:41:58.166699+00:00
- Tiêu đề: E-invoice settings: MISA HSM warning and issue mode (HSM signed / draft for manual signing / none)
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator (2026-11-07): trên trang cài đặt hóa đơn điện tử (MISA) phải cảnh báo rõ là cần mua HSM của MISA mới ký số tự động được, và cho chọn một trong ba: (1) đã mua HSM — ký số tự động; (2) chưa mua HSM — xuất hóa đơn nháp, duyệt thủ công; (3) không xuất hóa đơn tự động. Chọn (1) được lưu lại làm ghi nhận doanh nghiệp xác nhận đã mua HSM (không bắt buộc, chỉ lưu vết). Đặt trên trang Hóa đơn điện tử (operator chọn), form đơn hàng giữ nguyên.

Sửa:
- `CustomerInvoiceSettings`: IssueMode, HsmConfirmedAt, HsmConfirmedBy; `Mode` (NULL ⇒ SIGNED nếu AutoIssueEnabled, ngược lại NONE — cài đặt cũ không phải đổi dữ liệu). `InvoiceIssueModes` SIGNED/DRAFT/NONE.
- `OrderInvoicer`: chế độ DRAFT ⇒ chỉ gọi tạo nháp (cùng invoiceCode cố định), đơn DRAFT với ghi chú "chờ duyệt và ký số thủ công", không email, không tự thử lại; 2034 khi tạo nháp ⇒ nháp đã có. SIGNED giữ như trước (lỗi ký ⇒ tự tạo nháp, change 86E87C).
- `EInvoiceController.Save`: nhận Template.IssueMode (không gửi ⇒ theo AutoIssueEnabled cũ), kiểm tra giá trị; AutoIssueEnabled = SIGNED|DRAFT; chọn SIGNED lần đầu ⇒ lưu HsmConfirmedAt/By (giữ nguyên khi đổi chế độ sau đó); thông báo theo chế độ.
- Trang Hóa đơn điện tử: hộp cảnh báo HSM (vàng), 3 lựa chọn dạng radio (ô có viền, hover cam nhạt, ô đang chọn xanh), hiện thời điểm đã xác nhận HSM; badge "Tự động xuất nháp" cho chế độ DRAFT; cập nhật "Cách hoạt động".

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `database/migrations/027-einvoice-issue-mode.sql`, `database/scripts/043-verify-einvoice-issue-mode.sql`, `database/scripts/044-rollback-einvoice-issue-mode.sql`
- MODIFY `src/TAPortal.Integrations/Tingee/OrderInvoices.cs`, `SqlOrderInvoiceStore.cs`
- MODIFY `src/TACustomerPortal.Web/Controllers/EInvoiceController.cs`, `Models/EInvoiceModels.cs`, `Views/EInvoice/Index.cshtml`, `wwwroot/css/customer.css`
- MODIFY `tests/TACustomerPortal.Tests/SafeCustomerEInvoiceTests.cs`

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- `027-einvoice-issue-mode.sql`: thêm 3 cột NULL + CK_CIS_IssueMode; không đổi dòng nào. Verify 043, rollback 044 (từ chối khi đã có lựa chọn/xác nhận).
- Execution: xem report

## Test và kết quả
- Release build 0 lỗi; Safe tests 487/487 (mới: InDraftModePaidOrdersGetADraftAndNothingIsSigned, TheSettingsPageWarnsAboutHsmAndOffersThreeWaysToIssue).
- Giao diện phần lựa chọn xem bằng trang thử dùng customer.css thật.

## Publish
- Integrations thay đổi ⇒ webhooks, invoice, portal. Xem report.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: xem report
- PR:
- Merge: xem report
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Nội dung cảnh báo HSM dựa trên tài liệu API MISA (phát hành qua API dùng HSM) và lỗi CallSignServiceFail thực tế; chưa có xác nhận chính thức từ MISA.
- Chế độ áp dụng cả khi bấm xuất tay trên trang đơn (DRAFT ⇒ tạo nháp).
