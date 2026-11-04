# Change TINGEE-INTEGRATION-CHG-20261104T073052Z-945D42

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261104T073052Z-945D42`
- Created UTC: 2026-11-04T07:30:52.244450+00:00
- Tiêu đề: Webhook site webhook.hoadontudong.vn (TAPortal.Webhooks): one /tingee address, auto-deploy target
- Trạng thái: IMPLEMENTED / TESTS_PASS

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
- Project:
- Destination (chỉ từ config):
- Trạng thái: NOT_PUBLISHED
- Artifact SHA256:
- Smoke test:

## Git
- Branch:
- Commit SHA:
- PR:
- Merge: PENDING_APPROVAL
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
