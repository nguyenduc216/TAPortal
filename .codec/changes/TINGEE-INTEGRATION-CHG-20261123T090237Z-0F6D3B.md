# Change TINGEE-INTEGRATION-CHG-20261123T090237Z-0F6D3B

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261123T090237Z-0F6D3B`
- Created UTC: 2026-11-23T09:02:37.063636+00:00
- Tiêu đề: E-invoice: Viettel SInvoice beside MISA, readiness check for automatic signed invoices (migration 067)
- Trạng thái: PARTIALLY_DEPLOYED (portal, invoice DEPLOYED; webhooks ROLLED_BACK vì lỗi đăng nhập SQL của server)

## Yêu cầu và phạm vi
Operator (2026-11-23), sau trao đổi về xuất hóa đơn qua MISA / VNPT / Viettel cho khách hàng của T.A (T.A là nền tảng, mỗi doanh nghiệp khách xuất hóa đơn đứng tên mình): "ok thêm vào hệ thống nào". Hai việc đã đề xuất:
1. Bật Viettel SInvoice trên Customer Portal qua đối tác hóa đơn hiện tại (đã hỗ trợ mã `s-invoice`), bên cạnh MISA meInvoice. VNPT không có ở đối tác: ngoài phạm vi (cần tích hợp riêng).
2. Kiểm tra mức sẵn sàng xuất tự động: cho doanh nghiệp thấy còn thiếu gì để xuất hóa đơn ký số tự động hoàn toàn.

Đã làm:
- `CustomerInvoiceProviders` (MISA meInvoice, Viettel SInvoice): tên, tên ngắn, sản phẩm ký số từ xa (MISA eSign / Viettel-CA), có gửi email qua API hay không (Viettel: không, theo tài liệu đối tác chỉ XCyber và MISA). Mọi thông báo của `OrderInvoicer`, trang Hóa đơn điện tử, trang đơn hàng và cài đặt thanh toán dùng tên nhà cung cấp đã chọn thay vì ghi cứng MISA.
- Kết nối: chọn phần mềm hóa đơn (radio). Đổi nhà cung cấp tạo tài khoản mới ở đối tác rồi gỡ tài khoản cũ (lỗi gỡ chỉ ghi log), xóa mẫu và tắt tự động (người bán khác). Nhà cung cấp ngoài danh sách bị từ chối (400).
- Viettel: không gọi gửi email (tránh lỗi 2007); trang ẩn ô gửi email và ghi chú.
- `InvoiceReadiness.Check`: tài khoản, mẫu, ký hiệu đúng năm (Thông tư 78: C26TAA chỉ dùng năm 2026; tháng 12 nhắc chuẩn bị ký hiệu năm sau; sang năm mới báo lỗi), chữ ký số từ xa (SIGNED + lần ký gần nhất; DRAFT; NONE). `IOrderInvoiceStore.SigningHealthAsync` đọc lần phát hành gần nhất và lần gần nhất phải tạo nháp vì không ký được (`InvoiceError LIKE 'Chưa ký số được%'`, tiền tố `OrderInvoiceRules.SigningFailedPrefix`).
- Trang Hóa đơn điện tử: thẻ "Sẵn sàng xuất tự động" (ok / cần chú ý / còn thiếu) và "Các bước để xuất hóa đơn tự động" (hợp đồng + Mẫu 01, mua chữ ký số từ xa loại ký tự động, đăng ký chứng thư với cơ quan thuế, liên kết với phần mềm hóa đơn, xem thử, chọn chế độ, đổi ký hiệu đầu năm). Nhãn chế độ: "Đã có / Chưa có chữ ký số từ xa".
- Migration 067 (schema): `CK_CIS_Provider` cho phép `'misa','s-invoice'`. Verify 121, rollback 122 (từ chối khi đã có doanh nghiệp dùng nhà cung cấp khác misa).

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `database/migrations/067-einvoice-viettel-provider.sql`, `database/scripts/121-verify-einvoice-viettel-provider.sql`, `database/scripts/122-rollback-einvoice-viettel-provider.sql`
- MODIFY `src/TAPortal.Integrations/Tingee/OrderInvoices.cs`, `src/TAPortal.Integrations/Tingee/SqlOrderInvoiceStore.cs`
- MODIFY `src/TACustomerPortal.Web/Controllers/EInvoiceController.cs`, `src/TACustomerPortal.Web/Models/EInvoiceModels.cs`, `src/TACustomerPortal.Web/Views/EInvoice/Index.cshtml`, `src/TACustomerPortal.Web/Views/Orders/Details.cshtml`, `src/TACustomerPortal.Web/Views/PaymentSettings/Index.cshtml`, `src/TACustomerPortal.Web/wwwroot/css/customer.css`
- MODIFY `src/TAPortal.Webhooks/Program.cs` (chú thích)
- MODIFY `tests/TACustomerPortal.Tests/SafeCustomerEInvoiceTests.cs`

## Migration SQL
- Database: TAPortalSys
- Script: `database/migrations/067-einvoice-viettel-provider.sql` (sau 066)
- Verify: `database/scripts/121-verify-einvoice-viettel-provider.sql`; Rollback: `database/scripts/122-rollback-einvoice-viettel-provider.sql`
- Lệnh: `Invoke-TAMigrations.ps1 -Plan "067:121:122"`
- Execution: **EXECUTED** 2026-11-23T10:46:11Z qua `Invoke-TAMigrations.ps1 -Plan "067:121:122"`: pre-check NOT_APPLIED → dry-run (migration PASS, rollback NOT_APPLIED, @@TRANCOUNT=0) → pre-check lại NOT_APPLIED → backup đầy đủ gần nhất 7990 phút nên tạo bản mới `TAPortalSys-predeploy-20261123T104309Z.bak` (RESTORE VERIFYONLY OK) → gates 829/829 → chạy thật VERIFY_STATUS=PASS. Log: `.codec/reports/TINGEE-INTEGRATION-CHG-20261123T090237Z-0F6D3B-migrations-20261123T104309Z.log`. Dry-run trước (`-DryRunOnly`, log `...-migrations-20261123T104257Z.log`) xác nhận 066 ALREADY_APPLIED.

## Test và kết quả
- `dotnet restore TAPortal.sln`; `dotnet build TAPortal.sln -c Release --no-restore`: 0 lỗi.
- Safe tests: 829/829 PASS. Mới: TheCompanyCanConnectViettelSInvoiceAndSwitchingProviderStartsOver, OnlyOfferedProvidersCanBeConnected, ViettelInvoicesAreIssuedWithoutTheEmailItCannotSend, ViettelRefusalsNameViettelNotMisa, TheSeriesYearFollowsThongTu78, TheChecklistShowsWhatStandsBetweenTheCompanyAndAutomaticSignedInvoices, ASigningFailureShowsOnTheChecklist.
- Codec tests OK; `codec.py validate`: chỉ lỗi đường dẫn publish cố định của workspace operator (checkout khác, đã biết; Deploy-TARemote coi là WARN). `git diff --check` OK.

## Publish
- Project: TAPortal.Web, TACustomerPortal.Web, TAPortal.Webhooks (Integrations thay đổi)
- Deploy 2026-11-23 (`Deploy-TARemote.ps1 -Target portal,invoice,webhooks -MigrationsApplied`), source commit 57bdaea (code d779fd0), audit PASS (829/829):
  - portal (TAPortal.Web): DEPLOYED 10:52:17Z, artifact b044d799c9932f289018448f2ba8062acfd300cd5816ab2a7d5a7fb160c29f56, package 7912C7F7B2C3DDAA53CA5E22AB3A878EFA46944BFF04E8BC664B07C19E7276E7
  - invoice (TACustomerPortal.Web): DEPLOYED 10:52:23Z, artifact 3584590fd9bd24e987dc635201e94132deea7d8f49ce2d4c2c86a8380734350c, package 15165C9FCE26BF8DFB7EFC555F889B8E4034EAF5DB8C5B32321E72C792EDB7A9
  - webhooks (TAPortal.Webhooks): ROLLED_BACK 10:54:35Z (health /health 503 trước và sau rollback), artifact 2b7d8c762851f99e5bcc0f67713dc9522fa861d61c109eb523ba828755c3c59e. Log server `C:\TADeploy\logspply-webhooks-*`.
  - Nguyên nhân (Event Log Application, không phải do thay đổi này): SQL login `TA_User` của các ứng dụng hết hạn mật khẩu ("Login failed for user 'TA_User'. Reason: The password of the account has expired."), thấy từ 17:45 giờ VN; mọi worker của portal và webhooks cùng lỗi. PENDING_OPERATOR: đổi/gia hạn mật khẩu login (thay đổi bảo mật, agent không tự làm), sau đó deploy lại webhooks.

## Git
- Branch: feature/einvoice-multi-provider (từ origin/main 70d8b7d)
- Commit SHA: (source commit, ghi ở report)
- Merge: xem report / hand-off

## Rủi ro, quyết định và việc còn lại
- Chưa kiểm chứng với tài khoản Viettel SInvoice thật: phát hiện "không ký được" (`IsSigningFailure`, chuỗi "SignService") được xác nhận cho MISA; với Viettel, lỗi ký có thể về FAILED kèm lý do thay vì tạo nháp. Kiểm tra bằng Admin "Hóa đơn Sandbox" với tài khoản SInvoice thật rồi bổ sung mã lỗi nếu cần.
- Viettel: tài khoản kết nối thường là tài khoản API riêng (xin Viettel cấp); hệ thống không gửi email hóa đơn cho người mua.
- VNPT Invoice: đối tác không hỗ trợ; cần tích hợp trực tiếp (web service SOAP) khi có tài liệu và tài khoản demo của VNPT.
