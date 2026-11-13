# Change ZALO-MINI-APP-CHG-20261113T053124Z-A0CC3C

- Feature ID: `ZALO-MINI-APP`
- Change ID: `ZALO-MINI-APP-CHG-20261113T053124Z-A0CC3C`
- Created UTC: 2026-11-13T05:31:24.275655+00:00
- Tiêu đề: Zalo Mini App: đăng nhập bằng số điện thoại Zalo, QR một lần, OTP ZNS xác minh số
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator 2026-11-13 (chat): Mini App chỉ đăng nhập bằng số điện thoại; SĐT Zalo phải trùng SĐT đăng ký của tài khoản TA Portal / Hóa đơn tự động.
Cách 1: quét QR trên web (mã một lần, 5 phút, chỉ mở cho Zalo có đúng SĐT của tài khoản). Cách 2: tên đăng nhập + mật khẩu và vẫn phải trùng SĐT.
Operator đồng ý 3 đề xuất: (1) cách 2 bắt buộc mật khẩu; (2) một SĐT nhiều tài khoản thì chọn tài khoản; (3) xác minh SĐT bằng OTP ZNS khi đăng ký/đổi số.
Phạm vi change này: nền tảng backend (DB, dịch vụ, API Mini App, OTP). Ngoài phạm vi: trang QR / OTP / hủy liên kết trên TA Portal và Hóa đơn tự động, ứng dụng Mini App (front-end), thông báo giao dịch có deep link, tra cứu giao dịch/hóa đơn.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
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

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path: `database/migrations/038-zalo-mini-app-auth.sql`
- Thứ tự chạy: sau 037
- Preflight / Verify / Rollback: `database/scripts/065-verify-zalo-mini-app-auth.sql` / `database/scripts/066-rollback-zalo-mini-app-auth.sql`
- Execution: **EXECUTED** 2026-11-13T06:37:51Z qua `deploy/windows/Invoke-TAMigrations.ps1 -Plan "038:065:066"` (operator: "làm nhé" sau đề xuất chạy migration + deploy). Pre-check NOT_APPLIED → dry-run (migration PASS, rollback NOT_APPLIED, @@TRANCOUNT=0) → pre-check lại NOT_APPLIED → backup đầy đủ gần nhất 138 phút (không cần tạo mới) → gates 717/717 → chạy thật VERIFY_STATUS=PASS. Log: `.codec/reports/ZALO-MINI-APP-CHG-20261113T053124Z-A0CC3C-migrations-20261113T063619Z.log`.

## Test và kết quả
- `dotnet restore TAPortal.sln`: OK.
- `dotnet build TAPortal.sln -c Release --no-restore`: 0 errors (5 warnings có sẵn, không thuộc code mới).
- `dotnet test --filter FullyQualifiedName~TACustomerPortal.Tests.Safe`: 700/700 passed (gồm 22 SafeZaloMiniAppTests).
- `python -m unittest discover -s .codec/tools -p test_codec.py`: OK.
- `git diff --check`: OK.

## Publish
- Project: TACustomerPortal.Web khi deploy (TAPortal.Integrations đổi nên cả TAPortal.Web, TAPortal.Webhooks)
- Destination (chỉ từ config):
- Trạng thái: NOT_PUBLISHED
- Artifact SHA256:
- Smoke test:

## Git
- Branch: feature/zalo-mini-app
- Commit SHA:
- PR:
- Merge: PENDING_APPROVAL
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Quyết định kỹ thuật: API đặt trong TACustomerPortal.Web (đã public HTTPS) thay vì site mới, tránh phải tạo IIS site; QR/OTP lưu ở DB chung nên cả hai portal tạo được.
- Bảo mật: SĐT luôn lấy từ Zalo (token phía client giải mã ở server bằng secret key), không nhận SĐT từ body; QR và OTP chỉ lưu hash; phiên Mini App ký/mã hóa bằng ASP.NET Data Protection, hết hạn 12 giờ, mất hiệu lực khi hủy liên kết hoặc tài khoản đổi SĐT.
- Endpoint graph.zalo.me (`/v2.0/me`, `/v2.0/me/info`) theo tài liệu Zalo Mini App; NOT_VERIFIED với Zalo thật vì chưa có Mini App ID/secret.
- CORS mặc định `https://h5.zdn.vn`, `zbrowser://h5.zdn.vn` (cấu hình được), cần xác nhận khi có Mini App thật.
- Việc operator: nâng OA lên gói Tăng trưởng, tài khoản ZBS, Zalo App, đăng ký mẫu ZNS OTP, tạo Mini App + xin quyền SĐT; sau đó cấu hình `ZaloMiniApp:AppId`, `ZaloMiniApp:SecretKey`, `ZaloMiniApp:OtpTemplateId` trên server (không commit secret).
- Còn lại: change tiếp theo làm UI QR/OTP/hủy liên kết trên 2 portal; migration 038 EXECUTED; chưa publish/deploy.
