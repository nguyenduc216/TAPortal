# CODEC DELIVERY REPORT — ZALO-MINI-APP-CHG-20261113T053124Z-A0CC3C

Feature ID: `ZALO-MINI-APP`
Change ID: `ZALO-MINI-APP-CHG-20261113T053124Z-A0CC3C`

1. Mục tiêu và kết quả thực tế: nền tảng backend đăng nhập Zalo Mini App (QR một lần, mật khẩu + trùng SĐT, chọn tài khoản, OTP ZNS xác minh SĐT): đã code và test; chưa chạy migration, chưa deploy.
2. Danh sách file cập nhật:
- ADD `database/migrations/038-zalo-mini-app-auth.sql` — 4 bảng mới (UserVerifiedPhones, PhoneOtpChallenges, ZaloMiniAppQrTokens, ZaloMiniAppLinks); không sửa dữ liệu cũ.
- ADD `database/scripts/065-verify-zalo-mini-app-auth.sql` — verify (PRINT, PASS/NOT_APPLIED).
- ADD `database/scripts/066-rollback-zalo-mini-app-auth.sql` — rollback: chỉ drop 4 bảng của 038.
- ADD `src/TAPortal.Integrations/ZaloMiniApp/ZaloMiniApp.cs` — options, chuẩn hóa SĐT, token QR/OTP (chỉ lưu hash).
- ADD `src/TAPortal.Integrations/ZaloMiniApp/ZaloGraph.cs` — đọc Zalo user id + SĐT đã xác thực từ graph.zalo.me.
- ADD `src/TAPortal.Integrations/ZaloMiniApp/SqlMiniAppStore.cs` — SQL store.
- ADD `src/TAPortal.Integrations/ZaloMiniApp/MiniAppAuth.cs` — QR một lần, đăng nhập mật khẩu + trùng SĐT, liên kết, chọn tài khoản, phiên ký bằng Data Protection.
- ADD `src/TAPortal.Integrations/ZaloMiniApp/PhoneVerification.cs` — OTP ZNS xác minh SĐT + DI.
- MODIFY `src/TAPortal.Integrations/Notifications/NotificationService.cs` — `SendSystemZnsAsync` (gửi ZNS qua kênh Zalo OA hệ thống với mẫu khác).
- ADD `src/TACustomerPortal.Web/Controllers/ZaloMiniAppController.cs` — API `/api/zalo-mini-app/*`.
- MODIFY `src/TACustomerPortal.Web/Program.cs` — đăng ký dịch vụ, CORS Mini App, rate limit `miniapp`.
- ADD `tests/TACustomerPortal.Tests/SafeZaloMiniAppTests.cs` — 22 test.
- MODIFY `tests/TACustomerPortal.Tests/SafeReleaseRegressionTests.cs` — thêm 6 endpoint ẩn danh đã review vào inventory.
3. Database `TAPortalSys`: `database/migrations/038-zalo-mini-app-auth.sql` (sau 037), verify 065, rollback 066: NOT_EXECUTED.
4. Build/test:
- `dotnet restore TAPortal.sln`: OK.
- `dotnet build TAPortal.sln -c Release --no-restore`: 0 errors (5 warnings có sẵn, không thuộc code mới).
- `dotnet test --filter FullyQualifiedName~TACustomerPortal.Tests.Safe`: 700/700 passed (gồm 22 SafeZaloMiniAppTests).
- `python -m unittest discover -s .codec/tools -p test_codec.py`: OK.
- `git diff --check`: OK.
5. Publish: NOT_PUBLISHED / NOT_DEPLOYED (chờ UI và cấu hình Zalo).
6. Git: branch `feature/zalo-mini-app`; commit SHA ở hand-off; merge PENDING (chưa phải bản giao hoàn chỉnh).
7. Rủi ro/rollback/việc operator cần làm:
- Quyết định kỹ thuật: API đặt trong TACustomerPortal.Web (đã public HTTPS) thay vì site mới, tránh phải tạo IIS site; QR/OTP lưu ở DB chung nên cả hai portal tạo được.
- Bảo mật: SĐT luôn lấy từ Zalo (token phía client giải mã ở server bằng secret key), không nhận SĐT từ body; QR và OTP chỉ lưu hash; phiên Mini App ký/mã hóa bằng ASP.NET Data Protection, hết hạn 12 giờ, mất hiệu lực khi hủy liên kết hoặc tài khoản đổi SĐT.
- Endpoint graph.zalo.me (`/v2.0/me`, `/v2.0/me/info`) theo tài liệu Zalo Mini App; NOT_VERIFIED với Zalo thật vì chưa có Mini App ID/secret.
- CORS mặc định `https://h5.zdn.vn`, `zbrowser://h5.zdn.vn` (cấu hình được), cần xác nhận khi có Mini App thật.
- Việc operator: nâng OA lên gói Tăng trưởng, tài khoản ZBS, Zalo App, đăng ký mẫu ZNS OTP, tạo Mini App + xin quyền SĐT; sau đó cấu hình `ZaloMiniApp:AppId`, `ZaloMiniApp:SecretKey`, `ZaloMiniApp:OtpTemplateId` trên server (không commit secret).
- Còn lại: change tiếp theo làm UI QR/OTP/hủy liên kết trên 2 portal; migration 038 NOT_EXECUTED; chưa publish/deploy.
8. Bằng chứng: output test ở mục 4; không có secret/PII.
