# Change TINGEE-INTEGRATION-CHG-20261104T080429Z-668347

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261104T080429Z-668347`
- Created UTC: 2026-11-04T08:04:29.413109+00:00
- Tiêu đề: Webhook API key authentication and a health/status page for the webhook site
- Trạng thái: DEPLOYED (webhooks, portal, invoice); API key bật

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
- DEPLOYED qua `Deploy-TARemote.ps1 -Target webhooks,portal,invoice` (audit PASS), source 271c735, 2026-11-04T08:07Z:
  - webhooks: artifact 0de63df6dd8b9f32d95f94f0de4ab618b46643db62531acb86e6c877e7a0d059, package DAB2EA8DDF9155F8A203C9FFB49B3674785B0D5780963B537B4B39802887D3B1, backup C:\TADeploy\backups\webhooks-20261104-150649, health 200.
  - portal: artifact 730732101c8fca95bb89bc547c4d4184e281e7bf62f8f8036bdeff901f0ed092, package 1D6E7B7BC9E6A1D4727C48CDDA9DE24C65454A20D75C27C6C8355274B27BC435, backup C:\TADeploy\backups\portal-20261104-150700, health 200.
  - invoice: artifact 66f10316204b1096926df223a65c12a315db28412f8a62ed57511d2bfc34f1ba, package 1DE081053840F36A54299053E97F0C1DA7C7E47E9A8A4A75B4B3EA91B465769D, backup C:\TADeploy\backups\invoice-20261104-150709, health 200.
- Cấu hình (agent, theo yêu cầu operator): API key ngẫu nhiên 256 bit sinh trên server, ghi `Tingee.WebhookApiKey/WebhookApiKeyHeader/WebhookBaseUrl` vào appsettings.json của site webhook và Portal (bản sao `.bak-20261104150742`), recycle pool TA.Webhooks + TA.Portal; bản cho operator: `C:\TADeploy\secrets\tingee-webhook-api-key.txt` (chỉ Administrators). Khóa không đi qua chat/Git.
- Smoke: /health 200 {"status":"ok","database":"ok","tingeeKeys":"configured","apiKey":"required (x-api-key)",...,"version":"1.0.0+271c735…"}; trang / 200. Kiểm tra trên server: ký nhưng không API key → 401; API key không ký → 200; API key + ký → 200; sai API key + ký → 401.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: 271c735 (source)
- PR:
- Merge: PENDING_APPROVAL
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
