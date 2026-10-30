# Change TINGEE-INTEGRATION-CHG-20261029T200714Z-B90483

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261029T200714Z-B90483`
- Created UTC: 2026-10-29T20:07:14Z
- Tiêu đề: Scaffold TAPortal.Integrations and TAPortal.Webhooks projects
- Trạng thái: IMPLEMENTED / BUILD_VERIFIED / NOT_PUBLISHED / PRODUCTION_NOT_DEPLOYED
- Agent: Claude Code (claude-opus-5-5)

## Yêu cầu và phạm vi
Operator duyệt thêm 2 project tên `TAPortal.Integrations` và `TAPortal.Webhooks` và thêm vào VS Code workspace. Change này chỉ dựng nền: ký/xác thực Tingee, client, endpoint webhook an toàn. Chưa có bảng inbox nên endpoint trả 503 (Tingee tự gửi lại) thay vì xác nhận mà không lưu.

## File thay đổi
- ADD src/TAPortal.Integrations/TAPortal.Integrations.csproj
- ADD src/TAPortal.Integrations/IntegrationsServiceCollectionExtensions.cs
- ADD src/TAPortal.Integrations/Tingee/TingeeOptions.cs, TingeeSignature.cs, TingeeClient.cs, TingeeWebhook.cs
- ADD src/TAPortal.Webhooks/TAPortal.Webhooks.csproj, Program.cs, TingeeWebhookEndpoints.cs, appsettings.json (không secret)
- ADD tests/TACustomerPortal.Tests/SafeTingeeIntegrationTests.cs
- MODIFY TAPortal.sln, tests/TACustomerPortal.Tests/TACustomerPortal.Tests.csproj (alias `webhooks`), src/TA.Portal-workspace.code-workspace (thêm 2 folder + dotnet.defaultSolution)

## Thiết kế
- Chữ ký: hex thường của HMAC_SHA512("{timestamp}:{body}", secret); body rỗng ký "{}"; timestamp yyyyMMddHHmmssfff giờ UTC+7; so sánh thời gian hằng (FixedTimeEquals).
- Webhook: kiểm tra độ mới (mặc định ±10 phút), chữ ký trên body gốc, lưu inbox rồi mới trả `{"code":"00","message":"Success"}`; 401 khi sai chữ ký/quá hạn; 503 khi chưa cấu hình key hoặc inbox lỗi; giới hạn body 64 KB (Kestrel + IIS).
- Secret chỉ từ biến môi trường `Tingee__ClientId`, `Tingee__SecretKey`.

## Migration SQL
- NONE_REQUIRED. Không chạy SQL.

## Test và kết quả
- dotnet restore TAPortal.sln: OK
- dotnet build TAPortal.sln -c Release --no-restore: 0 warning / 0 error
- Safe tests: 269 passed / 0 failed / 0 skipped (thêm 9 test Tingee)
- Codec unit tests 4/4 OK; codec validate PASS; git diff --check sạch
- Lỗi thật phát hiện khi build và đã sửa: handler `HttpContext` bị bind thành RequestDelegate (ASP0016) làm mất IResult; định dạng mili giây `SSS` (Java) đổi thành `fff`.

## Publish
- TAPortal.Webhooks: NOT_RUN — chưa có target trong `.codec/config.json`; thêm target cần sửa config (deploy.targets + shared_dependents) với phê duyệt operator.
- TAPortal.Web, TACustomerPortal.Web: NOT_REQUIRED (không đổi source/dependency).
- NOT_DEPLOYED; IIS NOT_CHECKED.

## Git
- Branch: feature/customer-portal-mvp01
- Base SHA: 47c5689aa7f19d15b4f5f2ab446f56e0169bc145
- Source SHA: f7979e10a7ce69174332db638c5ef1fec95369b4
- Delivery SHA: báo sau push
- Merge: PENDING_APPROVAL (PR #7 chưa merge)

## Rủi ro, quyết định và việc còn lại
- Tingee ký webhook trên JSON.stringify(body); ta xác thực trên body gốc nhận được. Cần xác minh với key UAT thật (PENDING_OPERATOR).
- Inbox SQL, worker, các bảng nghiệp vụ: các change sau (P1b+).
- Tingee Master Merchant + key UAT: PENDING_OPERATOR.
