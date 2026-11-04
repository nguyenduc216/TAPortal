# Change TINGEE-INTEGRATION-CHG-20261104T080429Z-668347

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261104T080429Z-668347`
- Created UTC: 2026-11-04T08:04:29.413109+00:00
- Tiêu đề: Webhook API key authentication and a health/status page for the webhook site
- Trạng thái: IMPLEMENTED / TESTS_PASS

## Yêu cầu và phạm vi
Operator (2026-11-04): tạo chứng thực cho webhook (Tingee endpoint → "API key tuỳ chỉnh", header `x-api-key`) và một trang health để kiểm tra trạng thái.
- `Tingee:WebhookApiKey` (+ `WebhookApiKeyHeader`, mặc định `x-api-key`): khi đặt thì mọi webhook phải có header khớp (so sánh hằng thời gian); nếu request có `x-signature` thì chữ ký vẫn phải đúng và còn hạn; request không ký chỉ được nhận khi có API key đúng (phòng khi Tingee không ký ở chế độ API key). Không đặt key ⇒ như cũ (bắt buộc chữ ký).
- App webhook: `/health` trả JSON (status, database, khóa Tingee, API key, chữ ký, webhook gần nhất, địa chỉ nhận, phiên bản, giờ khởi động), 503 khi thiếu khóa hoặc không tới được DB; `/` hiển thị cùng thông tin dạng trang, tự làm mới 30 giây. Không lộ khóa/chuỗi kết nối/payload.
- Sandbox "Gửi webhook thử" gửi kèm API key nếu Portal có cấu hình.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TAPortal.Integrations/Tingee/{TingeeOptions,TingeeWebhookEndpoints,TingeeWebhook,SqlTingeeWebhookInbox}.cs` (Integrations ⇒ portal, invoice, webhooks).
- ADD `src/TAPortal.Webhooks/WebhookHealth.cs`; MODIFY `src/TAPortal.Webhooks/Program.cs`.
- MODIFY `src/TAPortal.Web/Controllers/TingeeSandboxController.cs`.
- MODIFY tests `SafeTingeeIntegrationTests.cs` (+6).

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path:
- Thứ tự chạy:
- Preflight / Verify / Rollback:
- Execution: NOT_APPLICABLE

## Test và kết quả
- Build Release 0 lỗi; Safe tests 363/363 (working tree).

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
