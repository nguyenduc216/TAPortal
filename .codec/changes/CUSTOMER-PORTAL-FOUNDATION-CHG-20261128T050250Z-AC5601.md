# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261128T050250Z-AC5601

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261128T050250Z-AC5601`
- Created UTC: 2026-11-28T05:02:50.547277+00:00
- Tiêu đề: E-invoice: demo and production connections side by side, template list picks the default, Add template button
- Trạng thái: DEPLOYED

## Yêu cầu và phạm vi
Operator (2026-11-28), trang Hóa đơn điện tử:
- Danh sách mẫu chỉ để chọn mặc định; thêm mẫu mới bằng nút riêng. Hiện tại chọn "Thêm mẫu mới" vẫn giữ mẫu số và ký hiệu cũ, và mục được chọn không được tô sáng.
- Tách môi trường demo và production của SoftDreams, chuyển qua lại được.

Quyết định:
- Danh sách mẫu: radio chọn mặc định, mục đang chọn được tô sáng. Nút "Thêm mẫu" mở hộp thoại (mẫu số, ký hiệu, tùy chọn "Dùng làm mẫu mặc định") gọi POST AddTemplate. Khi đã có danh sách thì ẩn ô nhập tay; ô nhập tay chỉ còn cho mẫu đầu tiên và cho MISA/Viettel.
- Môi trường: bảng mới dbo.CustomerInvoiceConnections (migration 071) giữ đăng nhập (mật khẩu mã hóa) theo nhà cung cấp và môi trường. Kết nối đang dùng vẫn nằm ở CustomerInvoiceSettings nên bộ xuất hóa đơn không đổi.
  - Chuyển môi trường (POST SwitchEnvironment): lưu kết nối đang dùng, nạp kết nối và mẫu mặc định của môi trường kia. Có hộp xác nhận nói rõ hóa đơn thật hay thử.
  - Kết nối môi trường mới (Index?env=LIVE|DEMO) giữ lại môi trường cũ.
- CustomerInvoiceTemplates.IsDefault: mẫu mặc định riêng từng môi trường.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `database/migrations/071-einvoice-environments.sql`, `database/scripts/129-verify-einvoice-environments.sql`, `database/scripts/130-rollback-einvoice-environments.sql`
- MODIFY `src/TAPortal.Integrations/Tingee/OrderInvoices.cs` (InvoiceConnection, IsDefault, phương thức store), `SqlOrderInvoiceStore.cs`
- MODIFY `src/TACustomerPortal.Web/Controllers/EInvoiceController.cs` (Index env, AddTemplate, SwitchEnvironment, KeepActiveConnection), `Models/EInvoiceModels.cs`, `Views/EInvoice/Index.cshtml`, `wwwroot/css/customer.css`, `wwwroot/js/customer.js`
- MODIFY `tests/TACustomerPortal.Tests/SafeCustomerEInvoiceTests.cs`, `SafeEasyInvoiceTests.cs`

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script: `database/migrations/071-einvoice-environments.sql`
- Thứ tự chạy: sau 070
- Preflight / Verify / Rollback: `129-verify-einvoice-environments.sql` / `130-rollback-einvoice-environments.sql`. Thêm 1 bảng và 1 cột có default, không đổi dòng nào.
- Execution: EXECUTED 2026-11-28 05:11:32Z qua `Invoke-TAMigrations.ps1 -Plan "071:129:130"` tại HEAD 8036749. Pre-check NOT_APPLIED; dry-run afterMigration=PASS, afterRollback=NOT_APPLIED, trancount 0; backup: dùng bản full gần nhất (946 phút); gates PASS; real run VERIFY_STATUS=PASS. Log: `.codec/reports/CUSTOMER-PORTAL-FOUNDATION-CHG-20261128T050250Z-AC5601-migrations-20261128T050926Z.log`.

## Test và kết quả
- Build 0 lỗi. Safe tests 873 PASS (test mới DemoAndProductionAreKeptSideBySideAndSwitched; cập nhật test danh sách mẫu).

## Publish
- Project: TACustomerPortal.Web (Integrations đổi; portal/webhooks không dùng phần mới)
- Destination (chỉ từ config): invoice (TA.Invoice)
- Trạng thái: DEPLOYED 2026-11-28 05:16Z, source commit 420cf01, `Deploy-TARemote.ps1 -Target invoice -MigrationsApplied`, health HTTP 200 (lần đầu bị audit chặn vì dòng trống cuối customer.js, đã sửa)
- Artifact SHA256: 500e6cf44891655c1ea947604738fe285476b7150fb6c7de0869523c1c0f936b (package AF593EE560B99CA2C1477B9B4E459FF7149273E7EB4325385EEC40CBFDC69982)
- Backup trên server: `C:\TADeploy\backups\invoice-20261128-121615`
- Smoke test: health 200; audit gates PASS.

## Git
- Branch: feature/order-details-popups
- Commit SHA: 420cf01
- PR:
- Merge: MERGED fast-forward vào main (sau deploy)
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Hóa đơn đã xuất ở môi trường nào chỉ tải PDF được khi đang dùng môi trường đó (đơn chưa lưu môi trường đã xuất).
- Kết nối demo đang dùng trên production chưa có dòng trong bảng mới; trang tự hiện nó là "Đang dùng" và lần chuyển đầu tiên lưu nó lại.
