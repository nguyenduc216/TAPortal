# Change TINGEE-INTEGRATION-CHG-20261104T124547Z-45D0B5

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261104T124547Z-45D0B5`
- Created UTC: 2026-11-04T12:45:47.984272+00:00
- Tiêu đề: E-invoice Sandbox in Admin: provider accounts, templates, draft/issue, download and email via Tingee e-invoice API
- Trạng thái: DEPLOYED

## Yêu cầu và phạm vi
Operator (2026-11-04): kết nối hóa đơn điện tử qua Tingee cho khách hàng; trước hết đọc tài liệu xuất hóa đơn và tạo sandbox trên Portal (P6 của feature). Phần cài đặt hóa đơn cho khách hàng và tự xuất hóa đơn khi có giao dịch vào là các change sau.

Tài liệu đã đọc: https://developers.tingee.vn/docs/hoa-don-dien-tu (openapi v1.2.2, nhóm "Hóa đơn điện tử"). Luồng Tingee: get-provider → create-or-update-account (tài khoản tại XCyber/HiLo/SInvoice/MISA/BKAV, Tingee lưu mật khẩu mã hóa, kiểm tra đăng nhập — lỗi 2037) → invoice-templates (mẫu số + ký hiệu; HiLo cần invoiceNoToCheck, BKAV không hỗ trợ) → create (nháp, trả PDF base64) / create-and-issue (ký HSM, phát hành thật; invoiceCode do đối tác tự sinh, phát hành lại cùng mã = 2034) → download (PDF) / send-email (chỉ XCyber, MISA). Số tiền gửi dạng chuỗi; Tingee tự tính thuế/tổng. Có thêm nhóm hóa đơn đầu vào (input/*, chỉ x-cyber) — ngoài phạm vi.

Trong phạm vi:
- `TAPortal.Integrations`: `ITingeeInvoiceApi`/`TingeeInvoiceApi` (9 endpoint hóa đơn đầu ra), DTO, danh mục nhà cung cấp và thuế suất; cùng quy tắc ký, envelope và merchantId (Direct/Master) như `TingeeApi`. Interface riêng để không đổi `ITingeeApi`.
- Admin (`TAPortal.Web`): trang "Hóa đơn Sandbox" (menu Tingee, chỉ SYS_ADMIN): cấu hình/phạm vi merchant như Tingee Sandbox; danh sách + kết nối/cập nhật/xóa tài khoản nhà cung cấp (popup); lấy mẫu hóa đơn; form hóa đơn thử (người mua, 3 dòng hàng, thuế suất); "Tạo nháp" hiển thị PDF; "Phát hành" bắt buộc tích ô xác nhận; tải/xem PDF và gửi email theo mã hóa đơn. Gợi ý xử lý cho các mã lỗi 2032/2034/2035/2037/2038/2007/1057/1005.
- Không lưu gì trong database; mật khẩu nhà cung cấp chỉ gửi sang Tingee, không log, không hiển thị lại.

Ngoài phạm vi (change sau): cài đặt hóa đơn ở Customer Portal, tự xuất hóa đơn khi đơn thanh toán đủ (worker webhook), hóa đơn đầu vào.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `src/TAPortal.Integrations/Tingee/TingeeInvoiceApi.cs`
- MODIFY `src/TAPortal.Integrations/IntegrationsServiceCollectionExtensions.cs` (đăng ký `ITingeeInvoiceApi`)
- ADD `src/TAPortal.Web/Controllers/TingeeInvoiceSandboxController.cs`
- ADD `src/TAPortal.Web/Models/TingeeInvoiceSandboxModels.cs`
- ADD `src/TAPortal.Web/Views/TingeeInvoiceSandbox/Index.cshtml`
- MODIFY `src/TAPortal.Web/Views/Shared/_Layout.cshtml` (menu Tingee → Hóa đơn Sandbox)
- ADD `tests/TACustomerPortal.Tests/SafeAdminTingeeInvoiceSandboxTests.cs` (9 test)
- MODIFY `tests/TACustomerPortal.Tests/SafeAdminTingeeSandboxTests.cs` (thêm `Host()` dùng chung test host)

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Execution: NOT_APPLICABLE (không có thay đổi SQL)

## Test và kết quả
- `dotnet build TAPortal.sln -c Release`: 0 lỗi, 0 cảnh báo.
- Safe tests: 398/398 pass (gồm 9 test mới `SafeAdminTingeeInvoiceSandboxTests`). Working tree có thay đổi chưa commit của agent khác (CUSTOMER-PROFILE) — build/test chạy cả trên các file đó.
- Tingee thật: NOT_VERIFIED (cần tài khoản nhà cung cấp hóa đơn demo/test).

## Publish
- Project: TAPortal.Web (portal). Integrations đổi nhưng chỉ Admin dùng `ITingeeInvoiceApi`; Customer/Webhooks không đổi hành vi — republish khi deploy lần sau.
- Trạng thái: xem report.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: 43f9b10
- PR:
- Merge: PENDING_APPROVAL
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- "Phát hành" với tài khoản nhà cung cấp thật tạo hóa đơn điện tử thật (không xóa được): trang cảnh báo, nút bị khóa đến khi tích xác nhận, server cũng kiểm tra.
- Việc tiếp theo: (1) Customer Portal — trang cài đặt hóa đơn điện tử (nhà cung cấp, MST, tài khoản, mẫu/ký hiệu mặc định), cần migration lưu cấu hình (không lưu mật khẩu); (2) tự xuất hóa đơn khi đơn QR thanh toán đủ (worker trong TAPortal.Webhooks, idempotent theo invoiceCode = mã đơn).
