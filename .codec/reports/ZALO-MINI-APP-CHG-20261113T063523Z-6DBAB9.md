# CODEC DELIVERY REPORT — ZALO-MINI-APP-CHG-20261113T063523Z-6DBAB9

Feature ID: `ZALO-MINI-APP`
Change ID: `ZALO-MINI-APP-CHG-20261113T063523Z-6DBAB9`

1. Mục tiêu và kết quả thực tế: trang Zalo trên TA Portal và Hóa đơn tự động (đổi SĐT, OTP, QR, hủy liên kết) và cổng xác minh SĐT; đã code và test.
2. Danh sách file cập nhật:
- ADD `src/TAPortal.Integrations/ZaloMiniApp/ZaloAccountService.cs`: phần tài khoản dùng chung (panel, đổi SĐT, OTP, QR, hủy liên kết) và `PhoneVerificationGate`.
- MODIFY `src/TAPortal.Integrations/ZaloMiniApp/MiniAppAuth.cs`: tách việc tạo QR sang ZaloAccountService.
- MODIFY `src/TAPortal.Integrations/ZaloMiniApp/SqlMiniAppStore.cs`: `ChangePhoneAsync` (một transaction: cập nhật Users.Phone, hủy liên kết, ghi AuditLogs, không ghi SĐT vào audit).
- MODIFY `src/TAPortal.Integrations/ZaloMiniApp/ZaloMiniApp.cs`, `PhoneVerification.cs`, `ZaloGraph.cs`: tùy chọn `RequirePhoneVerification`, DI; đổi lời nhắn "liên hệ T.A" thành "liên hệ bộ phận hỗ trợ" (quy tắc thương hiệu Customer Portal).
- ADD `src/TACustomerPortal.Web/Controllers/ZaloController.cs`, `Views/Zalo/Index.cshtml`, `Views/Zalo/Qr.cshtml`.
- MODIFY `src/TACustomerPortal.Web/Program.cs` (MiniAppAuth chỉ đăng ký ở site có API; cổng xác minh), `Views/Shared/_Layout.cshtml`, `Views/Profile/Personal.cshtml`, `wwwroot/js/customer.js` (đếm ngược không reload trang dựng bằng POST).
- ADD `src/TAPortal.Web/Controllers/AccountZaloActions.cs` (partial AccountController), `Views/Account/Zalo.cshtml`, `Views/Account/ZaloQr.cshtml`.
- MODIFY `src/TAPortal.Web/Controllers/AccountController.cs` (partial), `Program.cs` (AddZaloMiniApp, cổng xác minh), `Views/Shared/_Layout.cshtml`, `Views/Account/Profile.cshtml`.
- ADD `tests/TACustomerPortal.Tests/SafeZaloMiniAppPageTests.cs` (7 test render/API/CORS/antiforgery); MODIFY `SafeZaloMiniAppTests.cs` (+5 test).
3. Database `TAPortalSys`: không có migration mới (dùng 038).
4. Build/test:
- `dotnet build TAPortal.sln -c Release`: 0 errors.
- `dotnet test --filter FullyQualifiedName~TACustomerPortal.Tests.Safe`: 717/717 passed (34 test Zalo).
- `python -m unittest discover -s .codec/tools -p test_codec.py`: OK. `git diff --check`: OK.
- Lỗi thật test bắt được và đã sửa: (1) TempData đổi chuỗi GUID thành Guid nên ô nhập OTP không hiện (`as string` thành `?.ToString()`); (2) Admin không dựng được MiniAppAuth khi kiểm tra DI (chưa có IMiniAppPasswordCheck), nên MiniAppAuth chỉ đăng ký ở Customer Portal.
5. Publish: project, destination đã duyệt, artifact SHA256, status:
6. Git: branch, commit SHA, PR, merge SHA hoặc PENDING_APPROVAL:
7. Rủi ro/rollback/việc operator cần làm:
- Trang Zalo hiện ngay cho mọi người dùng; khi chưa cấu hình Zalo thì trang ghi rõ "chưa bật OTP" / "ứng dụng đang được chuẩn bị". Đổi SĐT hoạt động ngay.
- Cổng xác minh mặc định TẮT; operator bật `ZaloMiniApp:RequirePhoneVerification=true` sau khi gửi thử OTP thành công.
- Không kiểm tra trực quan bằng trình duyệt (máy dev không có DB); HTML được kiểm tra bằng test render đầy đủ hai site.
8. Bằng chứng: output test ở mục 4; không có secret/PII.
