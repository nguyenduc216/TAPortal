# Change TINGEE-INTEGRATION-CHG-20261104T073052Z-945D42

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261104T073052Z-945D42`
- Created UTC: 2026-11-04T07:30:52.244450+00:00
- Tiêu đề: Webhook site webhook.hoadontudong.vn (TAPortal.Webhooks): one /tingee address, auto-deploy target
- Trạng thái: DEPLOYED (webhooks, portal, invoice)

## Yêu cầu và phạm vi
Operator (2026-11-04): tách mọi webhook của hệ thống hóa đơn tự động ra project/domain riêng để deploy Admin/Customer không vô tình làm hỏng việc nhận webhook; domain `webhook.hoadontudong.vn`, đường dẫn `/tingee/...` (không cần `/hooks`), Portal cập nhật theo, toàn bộ thống nhất một URL; deploy webhook tự động như portal/invoice. Operator tạo DNS A `webhook` → 113.160.249.61 và yêu cầu agent tự tạo thư mục, IIS site và SSL trên server ("bạn sẽ tạo folder, cài iis và ssl trên server luôn chứ, tôi chỉ cần cài đặt trên domain").
- Một URL: `https://webhook.hoadontudong.vn/tingee/{payment,va-status,bank-link}` (`TingeeOptions.WebhookBaseUrl`, mặc định domain này; `WebhookUrl(kind)`; `EffectiveVaStatusWebhookUrl` cho create-va của VCB/TPBank khi không đặt VaStatusWebhookUrl riêng).
- `MapTingeeWebhooks(prefix = "/tingee")` + `AllowAnonymous()` (xác thực bằng chữ ký HMAC). App TAPortal.Webhooks nhận ở `/tingee`. Portal nhận cả `/tingee` và đường dẫn cũ `/hooks/tingee` (chỉ tới khi endpoint trên Tingee đổi sang domain mới, gỡ ở lần sau).
- Sandbox Admin hiển thị/copy URL domain mới; "Gửi webhook thử" gửi tới app webhook. Customer BanksController dùng `EffectiveVaStatusWebhookUrl`.
- Deploy: `Deploy-TARemote.ps1 -Target webhooks` (TAPortal.Webhooks, health `/health`); `.codec/config.json` remote targets + AGENTS.md cập nhật.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TAPortal.Integrations/Tingee/{TingeeOptions,TingeeWebhookEndpoints}.cs` (Integrations ⇒ deploy portal, invoice, webhooks).
- MODIFY `src/TAPortal.Web/{Program.cs (chỉ đoạn MapTingeeWebhooks; phần CUSTOMER-PROFILE chưa commit của người khác giữ nguyên ngoài commit), Controllers/TingeeSandboxController.cs}`, `src/TACustomerPortal.Web/Controllers/BanksController.cs`.
- MODIFY `deploy/windows/Deploy-TARemote.ps1`, `.codec/config.json`, `AGENTS.md`.
- MODIFY tests `SafeTingeeIntegrationTests.cs` (đường dẫn /tingee), `SafeAdminTingeeSandboxTests.cs` (URL domain webhook, đường dẫn cũ vẫn nhận).

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path:
- Thứ tự chạy:
- Preflight / Verify / Rollback:
- Execution: NOT_APPLICABLE

## Test và kết quả
- Build Release 0 lỗi; Safe tests 357/357 (working tree); codec unittest OK; codec validate chỉ còn lệch publish path của checkout này (như trước).

## Publish
- Server (agent, theo yêu cầu operator 2026-11-04, script `deploy/windows/server/Setup-TAWebhooksSite.ps1`): thư mục `H:\WEBDEPLOYED\T.A\webhooks`; appsettings.json tạo từ Database + Tingee của Portal (copy trên server, không in giá trị) + `WebhookBaseUrl`; app pool `TA.Webhooks` (No Managed Code, AlwaysRunning, không idle timeout); site `TA.Webhooks` (id 34) http + https `webhook.hoadontudong.vn`; chứng chỉ Let's Encrypt qua win-acme 2.2.9 (store WebHosting, gia hạn sau 2026-12-29); deploy target `webhooks` đăng ký trong `C:\TADeploy\deploy-config.json` (có bản .bak).
- Sửa tác vụ gia hạn SSL của server: "win-acme renew" trỏ vào bản wacs.exe tạm đã bị xóa (lần chạy 2026-11-04 09:28 lỗi 0x80070002) → tạo lại trỏ `C:\tools\win-acme\wacs.exe` (ảnh hưởng mọi site dùng Let's Encrypt; portal hết hạn 2026-12-05 nên cần gia hạn).
- Dry-run `-Target webhooks` PASS (lần đầu: audit từ merge-base nên gồm migration 008–016, đều đã EXECUTED ⇒ `-MigrationsApplied`).
- DEPLOYED qua `Deploy-TARemote.ps1 -Target webhooks,portal,invoice -MigrationsApplied` (audit PASS), source e04040d, 2026-11-04T07:40Z:
  - webhooks: artifact 1184f9d92bd937edf4d8d3a29eeb468119e8645d9abafaa5de58ee6a088665b4, package 88FD5480AC15090C3AAB1AFF4607AF273E389B99CD72D187F01E19912E923F88, backup C:\TADeploy\backups\webhooks-20261104-143947, health https://webhook.hoadontudong.vn/health 200.
  - portal: artifact 35930b90015b8e3c6688fd9c98666b9ead3378e12a508d56db26a2e0007ddc44, package B3C1EE78F6820B11E6B6F9DADE4BC1FC57276DFADEE85006BA6AC7B9379C9C73, backup C:\TADeploy\backups\portal-20261104-143954, health 200.
  - invoice: artifact 1c4c5036709156eb7e86720f29f5c41de1ed55608eb61d9c0aafefbcba03e534, package 2E708016C315EF39FF528EE42003C0F090A686F10AAD4BB74E5285644A35759F, backup C:\TADeploy\backups\invoice-20261104-144002, health 200.
- Smoke: POST không chữ ký → 401 (webhook /tingee/payment, portal /tingee/payment, portal /hooks/tingee/payment); webhook bank-link ký thật tới https://webhook.hoadontudong.vn/tingee/bank-link → 200 {"code":"00"}, lưu inbox Id=3.
- Còn lại: operator đổi URL endpoint trên Tingee sang https://webhook.hoadontudong.vn/tingee/payment; sau khi Tingee gửi thành công qua domain mới thì gỡ /hooks/tingee và /tingee khỏi Portal.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: e04040d (source)
- PR:
- Merge: PENDING_APPROVAL
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
