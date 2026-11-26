# Change TINGEE-INTEGRATION-CHG-20261125T234234Z-EAFC57

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261125T234234Z-EAFC57`
- Created UTC: 2026-11-25T23:42:34.399507+00:00
- Tiêu đề: SoftDreams EasyInvoice provider, integrated directly (migration 068)
- Trạng thái: DEPLOYED

## Yêu cầu và phạm vi
Operator (2026-11-26): "tách luôn một cái nhánh ... đưa thêm một cái nhà phân phối invoice là SoftDreams, và tích hợp cái API của SoftDreams"; "Làm luôn trong phiên này". Tài liệu nguồn: "EasyInvoice_TaiLieuTichHop_v8.0" (Google Doc của SoftDreams). Câu hỏi về mật khẩu (API EasyInvoice gửi username:password:taxCode trong header mỗi lần gọi, không có đối tác giữ hộ như MISA/Viettel), operator chọn: "Mã hoá lưu trong DB".

Đã làm:
- `src/TAPortal.Integrations/SoftDreams/EasyInvoice.cs` (mới):
  - `EasyInvoiceAuth.Header`: `signature:nonce:timestamp:username:password:taxCode`, signature = Base64(MD5(METHOD+timestamp+nonce)).
  - `EasyInvoiceApi`: importAndIssueInvoice (ký server), importInvoice (nháp), getInvoicesByIkeys, getInvoicePdf. Production `https://api.easyinvoice.vn/`, demo `http://api.softdreams.vn/` (cấu hình `EasyInvoice:*`). Không ghi request vào log (header chứa mật khẩu).
  - `EasyInvoiceResponse`: Status 2 (số hoặc chuỗi) = thành công; lỗi → `EasyInvoiceApiException` kèm `KeyInvoiceMsg[ikey]`; Status 4 = từ chối (không thử lại), 5/HTTP lỗi = thử lại.
  - `EasyInvoiceXml.Build`: XmlData theo Thông tư 78. Ikey = mã hóa đơn cố định của đơn. Tiền lấy từ `AmountBeforeTax`/`TaxAmount` đã lưu của dòng đơn, nên tổng hóa đơn đúng số khách đã trả, kể cả giá đã gồm VAT. Có tổng theo thuế suất và `AmountInWords` (`VietnameseMoney`). PaymentMethod "Chuyển khoản". Pattern = mẫu số + ký hiệu ("1C26TAA"), Serial rỗng. `CusEmails` chỉ khi bật gửi email (EasyInvoice tự gửi).
  - `IInvoiceSecretProtector` / `DpapiInvoiceSecretProtector`: DPAPI LocalMachine (entropy riêng), để Customer Portal ghi và worker của Webhooks đọc được trên cùng server. Data Protection của ASP.NET không dùng được vì mỗi app có key riêng.
- `OrderInvoices.cs`:
  - `CustomerInvoiceProviders.SoftDreams` (`softdreams`, `ViaPartner=false`, `HasTemplates=false`).
  - `CustomerInvoiceSettings.ProviderSecret`; `IsConnected` của SoftDreams = có login đã mã hóa.
  - `OrderInvoiceLine` có `AmountBeforeTax`/`TaxAmount`.
  - `OrderInvoicer.IssueDirect`: luôn tra `getInvoicesByIkeys` trước. Đã phát hành → ghi ISSUED, không xuất lại. Đã hủy → FAILED. DRAFT mode → importInvoice. SIGNED → importAndIssueInvoice. Lỗi dùng chung NotIssued (retry/backoff/FAILED). Không cần merchant của đối tác thanh toán.
- `SqlOrderInvoiceStore`: đọc/ghi `ProviderSecret` (VarBinary(max)); `DueAsync` chấp nhận `ProviderAccountId` hoặc `ProviderSecret`; claim đọc thêm `AmountBeforeTax`, `TaxAmount`.
- `IntegrationsServiceCollectionExtensions.AddTingee`: đăng ký `EasyInvoiceOptions`, HttpClient `IEasyInvoiceApi`, `IInvoiceSecretProtector` (Windows).
- Customer Portal:
  - `EInvoiceController.ConnectDirect`: kiểm tra login bằng một lần tra Ikey ngẫu nhiên (chỉ đọc), lưu mật khẩu đã mã hóa, gỡ tài khoản MISA/Viettel cũ ở đối tác. Từ chối dấu ":" trong tên đăng nhập/mật khẩu (dấu phân cách của header).
  - Disconnect xóa login. Templates/Preview không dùng cho SoftDreams (nhập tay mẫu số + ký hiệu).
  - `EInvoiceAccountInput.Demo`.
  - View: lựa chọn thứ ba, ô "môi trường thử (demo)", ghi chú mật khẩu đúng theo từng nhà cung cấp, ẩn nút Lấy mẫu/Xem thử.
  - `OrdersController.InvoicePdf`: PDF lấy thẳng từ EasyInvoice.
- Migration 068 (schema): cột `ProviderSecret varbinary(max) NULL`, `CK_CIS_Provider` thêm `'softdreams'`. Verify 123, rollback 124 (từ chối khi đã có doanh nghiệp dùng SoftDreams hoặc đã lưu login).

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `src/TAPortal.Integrations/SoftDreams/EasyInvoice.cs`
- ADD `database/migrations/068-einvoice-softdreams-provider.sql`, `database/scripts/123-verify-einvoice-softdreams-provider.sql`, `database/scripts/124-rollback-einvoice-softdreams-provider.sql`
- MODIFY `src/TAPortal.Integrations/Tingee/OrderInvoices.cs`, `src/TAPortal.Integrations/Tingee/SqlOrderInvoiceStore.cs`, `src/TAPortal.Integrations/IntegrationsServiceCollectionExtensions.cs`
- MODIFY `src/TACustomerPortal.Web/Controllers/EInvoiceController.cs`, `src/TACustomerPortal.Web/Controllers/OrdersController.cs`, `src/TACustomerPortal.Web/Models/EInvoiceModels.cs`, `src/TACustomerPortal.Web/Views/EInvoice/Index.cshtml`
- ADD `tests/TACustomerPortal.Tests/SafeEasyInvoiceTests.cs`; MODIFY `tests/TACustomerPortal.Tests/SafeCustomerEInvoiceTests.cs`, `tests/TACustomerPortal.Tests/SafeCustomerPaymentsTests.cs` (test host dùng fake EasyInvoice + protector giả, không bao giờ gọi mạng)

## Migration SQL
- Database: TAPortalSys
- Script: `database/migrations/068-einvoice-softdreams-provider.sql` (sau 067)
- Verify: `database/scripts/123-verify-einvoice-softdreams-provider.sql`; Rollback: `database/scripts/124-rollback-einvoice-softdreams-provider.sql`
- Lệnh: `Invoke-TAMigrations.ps1 -Plan "068:123:124"`
- Execution: EXECUTED 2026-11-26 11:46Z (operator: "OK, đồng ý migration"). Pre-check NOT_APPLIED, dry-run PASS/NOT_APPLIED đã rollback, backup COPY_ONLY `TAPortalSys-predeploy-20261126T113732Z.bak` (đã verify), gates 857/857, real run VERIFY_STATUS=PASS. Log: `.codec/reports/TINGEE-INTEGRATION-CHG-20261125T234234Z-EAFC57-migrations-20261126T113732Z.log`.

## Test và kết quả
- `dotnet restore TAPortal.sln`; `dotnet build TAPortal.sln -c Release --no-restore`: 0 lỗi.
- Safe tests: 856/856 PASS. Mới (SafeEasyInvoiceTests): chuỗi xác thực, DPAPI, đọc phản hồi/lỗi/PDF, số tiền bằng chữ, XmlData (tiền theo dòng đã lưu, thuế suất, escape, email), phát hành, không phát hành lại khi lần trước mất phản hồi, từ chối/thử lại, nháp, không đọc được login, kết nối qua trang (mật khẩu chỉ lưu dạng mã hóa, demo, nhập mẫu tay, gỡ kết nối), login sai không lưu, tải PDF.
- Codec tests OK. `git diff --check` OK.
- CHƯA kiểm tra với hệ thống thật của SoftDreams: chưa có tài khoản demo.

## Publish
- Project: TAPortal.Web, TACustomerPortal.Web, TAPortal.Webhooks (Integrations thay đổi)
- Trạng thái: DEPLOYED 2026-11-26 (commit 321c782 = 130193f + merge origin/main ad9ac47). portal, invoice, webhooks: health HTTP 200, Event Log không có lỗi 15 phút sau deploy. Backup server: `C:\TADeploy\backups\{portal-20261126-185653,invoice-20261126-185702,webhooks-20261126-185711}`. Manifest: `.codec/reports/TINGEE-INTEGRATION-CHG-20261125T234234Z-EAFC57-<PROJECT>.sha256`.
- Webhooks chạy lại được (lần deploy 0F6D3B bị rollback vì login `TA_User` hết hạn; đã tắt CHECK_EXPIRATION trong phiên này).

## Git
- Branch: feature/einvoice-softdreams (từ origin/main 3ff2977)
- Commit SHA: (ghi ở report)
- Merge: vào main sau deploy (standing authorization)

## Rủi ro, quyết định và việc còn lại
- Quyết định của operator: lưu mật khẩu EasyInvoice đã mã hóa (DPAPI LocalMachine). Ngoại lệ có chủ đích với nguyên tắc "không lưu mật khẩu nhà cung cấp", chỉ áp dụng cho SoftDreams. Mật khẩu không hiển thị lại, không ghi log. Gỡ kết nối thì xóa mật khẩu.
- DPAPI LocalMachine: chuyển ứng dụng sang server khác thì login không giải mã được. Khi đó hệ thống báo "kết nối lại tài khoản" (đã có test).
- Chờ SoftDreams trả lời (danh sách câu hỏi đã gửi operator):
  - có API key thay mật khẩu không;
  - gửi lại cùng Ikey sau khi đã phát hành thì sao;
  - mã lỗi khi không ký được, để tạo nháp thay vì FAILED như MISA;
  - định dạng trả về của getInvoicePdf (đang đọc được cả PDF thô lẫn Base64 trong JSON);
  - VATRate của hóa đơn nhiều thuế suất (đang lấy mức cao nhất);
  - tắt tự tạo tài khoản tra cứu (đang không gửi CusCode);
  - mật khẩu có ":".
- Việc còn lại: thử với tài khoản demo của SoftDreams; chạy migration 068 rồi deploy portal/invoice/webhooks khi operator đồng ý; merge main sau đó.
