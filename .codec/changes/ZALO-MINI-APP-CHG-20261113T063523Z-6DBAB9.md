# Change ZALO-MINI-APP-CHG-20261113T063523Z-6DBAB9

- Feature ID: `ZALO-MINI-APP`
- Change ID: `ZALO-MINI-APP-CHG-20261113T063523Z-6DBAB9`
- Created UTC: 2026-11-13T06:35:23.863399+00:00
- Tiêu đề: Zalo Mini App: trang Zalo trên TA Portal và Hóa đơn tự động (đổi SĐT, OTP, QR, hủy liên kết), cổng xác minh SĐT
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator 2026-11-13 ("làm nhé"): giao diện cho change trước trên cả hai site.
- Hóa đơn tự động: trang "Ứng dụng trên Zalo" (`/account/zalo`), có trong menu Tài khoản, menu người dùng và trang Thông tin cá nhân.
- TA Portal: trang "Zalo Mini App T.A" (`/Account/Zalo`), có trong menu người dùng và trang Hồ sơ cá nhân.
- Chức năng: đổi số điện thoại của chính mình (audit USER_PHONE_CHANGED, hủy liên kết Zalo cũ); gửi/nhập OTP Zalo; mở mã QR một lần (đếm ngược 5 phút, tạo mã mới); hủy liên kết Zalo.
- Cổng xác minh SĐT (quyết định "buộc xác minh ở lần đăng nhập web tiếp theo"): chỉ bật khi `ZaloMiniApp:RequirePhoneVerification=true` VÀ đã có `OtpTemplateId`, để không khóa ai trước khi ZNS chạy được.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `src/TAPortal.Integrations/ZaloMiniApp/ZaloAccountService.cs`: phần tài khoản dùng chung (panel, đổi SĐT, OTP, QR, hủy liên kết) và `PhoneVerificationGate`.
- MODIFY `src/TAPortal.Integrations/ZaloMiniApp/MiniAppAuth.cs`: tách việc tạo QR sang ZaloAccountService.
- MODIFY `src/TAPortal.Integrations/ZaloMiniApp/SqlMiniAppStore.cs`: `ChangePhoneAsync` (một transaction: cập nhật Users.Phone, hủy liên kết, ghi AuditLogs, không ghi SĐT vào audit).
- MODIFY `src/TAPortal.Integrations/ZaloMiniApp/ZaloMiniApp.cs`, `PhoneVerification.cs`, `ZaloGraph.cs`: tùy chọn `RequirePhoneVerification`, DI; đổi lời nhắn "liên hệ T.A" thành "liên hệ bộ phận hỗ trợ" (quy tắc thương hiệu Customer Portal).
- ADD `src/TACustomerPortal.Web/Controllers/ZaloController.cs`, `Views/Zalo/Index.cshtml`, `Views/Zalo/Qr.cshtml`.
- MODIFY `src/TACustomerPortal.Web/Program.cs` (MiniAppAuth chỉ đăng ký ở site có API; cổng xác minh), `Views/Shared/_Layout.cshtml`, `Views/Profile/Personal.cshtml`, `wwwroot/js/customer.js` (đếm ngược không reload trang dựng bằng POST).
- ADD `src/TAPortal.Web/Controllers/AccountZaloActions.cs` (partial AccountController), `Views/Account/Zalo.cshtml`, `Views/Account/ZaloQr.cshtml`.
- MODIFY `src/TAPortal.Web/Controllers/AccountController.cs` (partial), `Program.cs` (AddZaloMiniApp, cổng xác minh), `Views/Shared/_Layout.cshtml`, `Views/Account/Profile.cshtml`.
- ADD `tests/TACustomerPortal.Tests/SafeZaloMiniAppPageTests.cs` (7 test render/API/CORS/antiforgery); MODIFY `SafeZaloMiniAppTests.cs` (+5 test).

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path: không có migration mới; dùng 038 của ZALO-MINI-APP-CHG-20261113T053124Z-A0CC3C
- Thứ tự chạy:
- Preflight / Verify / Rollback:
- Execution: NOT_EXECUTED

## Test và kết quả
- `dotnet build TAPortal.sln -c Release`: 0 errors.
- `dotnet test --filter FullyQualifiedName~TACustomerPortal.Tests.Safe`: 717/717 passed (34 test Zalo).
- `python -m unittest discover -s .codec/tools -p test_codec.py`: OK. `git diff --check`: OK.
- Lỗi thật test bắt được và đã sửa: (1) TempData đổi chuỗi GUID thành Guid nên ô nhập OTP không hiện (`as string` thành `?.ToString()`); (2) Admin không dựng được MiniAppAuth khi kiểm tra DI (chưa có IMiniAppPasswordCheck), nên MiniAppAuth chỉ đăng ký ở Customer Portal.

## Publish
- Project: TAPortal.Web, TACustomerPortal.Web, TAPortal.Webhooks (TAPortal.Integrations đổi)
- Destination (chỉ từ config):
- Trạng thái: DEPLOYED
- DEPLOYED 2026-11-13 06:41-06:42Z qua `deploy/windows/Deploy-TARemote.ps1 -Target portal,invoice,webhooks -MigrationsApplied` (audit ddfe35a..d4946e0: 8 commit, 54 file, 717/717 Safe tests, AUDIT PASSED). Source commit d4946e0.
  - portal: artifact 5eebe89086bcf9bba220919716985f9067640af10754c92a77924fdb793c860c, package 9948C070D8BC6C3B59DFB7E6A2AD11B3E937CFCF99BC41426EEC5EAD407729D8, health 200.
  - invoice: artifact af3dc68580f69ebcb5a91d1c94396f911062d0cd83a001d6bcc4e9ea17901f0e, package E9A367FDC2F77921838B4C30B17C31B3B8B2C6A2372D29EEBE7A26A77FAC916A, health 200.
  - webhooks: artifact 29524683cdbd280137a833a140c49e803ebd4bde512b8c2c02c98cdc1c7e5613, package 0F763E1850A17E8D207BCF943994942A28D44E0579AA48A3224F118916886513, health 200.
- Smoke: `/account/zalo` và `/Account/Zalo` chuyển về đăng nhập (302); `POST /api/zalo-mini-app/resume` trả 400 "Ứng dụng Zalo chưa được cấu hình" (đúng, server chưa có ZaloMiniApp:*).
- Lần chạy đầu bị từ chối ngay ở bước kiểm tra tham số (`-File` truyền danh sách target thành một chuỗi); không có gì được upload.
- Artifact SHA256:
- Smoke test:

## Git
- Branch: feature/zalo-mini-app
- Commit SHA: d4946e0 (source)
- PR:
- Merge: PENDING_APPROVAL
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Trang Zalo hiện ngay cho mọi người dùng; khi chưa cấu hình Zalo thì trang ghi rõ "chưa bật OTP" / "ứng dụng đang được chuẩn bị". Đổi SĐT hoạt động ngay.
- Cổng xác minh mặc định TẮT; operator bật `ZaloMiniApp:RequirePhoneVerification=true` sau khi gửi thử OTP thành công.
- Không kiểm tra trực quan bằng trình duyệt (máy dev không có DB); HTML được kiểm tra bằng test render đầy đủ hai site.
