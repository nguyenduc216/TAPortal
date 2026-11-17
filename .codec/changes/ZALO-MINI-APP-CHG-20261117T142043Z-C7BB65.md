# Change ZALO-MINI-APP-CHG-20261117T142043Z-C7BB65

- Feature ID: `ZALO-MINI-APP`
- Change ID: `ZALO-MINI-APP-CHG-20261117T142043Z-C7BB65`
- Created UTC: 2026-11-17T14:20:43.534254+00:00
- Tiêu đề: Mẫu tin OTP Zalo OA: mã mẫu OTP cấu hình trong Admin, gửi thử, bật kênh không cần mẫu thanh toán
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi

Operator đã đăng ký và nâng gói Zalo OA. Việc đầu tiên là dựng mẫu tin mã OTP của hệ thống.

Operator duyệt ngày 2026-11-17 bốn điểm:
- **Nội dung:** dùng câu Zalo đề xuất cho "Template xác thực".
- **Luồng:** chỉ xác minh số điện thoại. Luồng này đã có từ trước (`PhoneVerification`, OTP 6 số, hiệu lực 5 phút).
- **Phạm vi:** cả TA Portal và Hóa đơn tự động.
- **Nơi cấu hình mã mẫu:** trong Admin, không sửa appsettings trên server.

Tài liệu chính thức (developers.zalo.me → ZBS Template Message, tên mới của ZNS):
- Endpoint `POST https://business.openapi.zalo.me/message/template`. Code đã gọi đúng endpoint này.
- "Template xác thực" (template_type 2) chỉ dùng tag 1. Bố cục: logo ở header, một thành phần OTP (tham số kiểu OTP, tối đa 10 ký tự) và một đoạn văn. Không có nút.
- Template xác thực chỉ gửi được qua số điện thoại, không gửi được qua UID.
- Mẫu cần Zalo duyệt trước khi gửi được.

Mẫu tin đã duyệt (operator tự tạo trên ZBS Account):
- Loại: Template xác thực, tag Transaction.
- Tên: "Mã xác thực tài khoản Hóa đơn tự động".
- Logo: logo OA, bản sáng và bản tối.
- OTP: `<otp>`, kiểu OTP, giá trị mẫu `123456`.
- Đoạn văn: "Tuyệt đối KHÔNG chia sẻ mã xác thực cho bất kỳ ai dưới bất kỳ hình thức nào. Mã xác thực có hiệu lực trong 5 phút."

Ngoài phạm vi: quên mật khẩu bằng OTP, OTP khi đăng ký, đăng nhập 2 bước.

## Quyết định kỹ thuật
- **Nơi lưu mã mẫu:** kênh Zalo OA hệ thống (SYSTEM) có thêm trường `otpTemplateId`, tên hiển thị "Mã mẫu ZNS OTP". Trường này nằm trong settings của kênh, không phải secret. App ID, secret và refresh token vẫn mã hóa DPAPI trong DB như trước.
- **Nguồn mã mẫu:** `IOtpTemplateSource`, bản production là `SystemOtpTemplate`. Thứ tự ưu tiên:
  1. Giá trị Admin, chỉ khi kênh đang bật.
  2. `ZaloMiniApp:OtpTemplateId`, dùng làm dự phòng.
  3. Khi store lỗi: ghi log `ZALO_OTP_TEMPLATE_UNAVAILABLE` rồi dùng cấu hình dự phòng.
- **Nơi dùng nguồn này:** `PhoneVerification` khi gửi mã, `ZaloAccountService.PanelAsync` khi quyết định có hiện nút gửi OTP, và `PhoneVerificationGate`. Cổng chỉ đọc nguồn sau các điều kiện rẻ (đã đăng nhập, đúng đường dẫn, chưa nhớ người dùng) để không truy vấn DB ở mỗi request.
- **Bật kênh không cần mẫu thanh toán:** với kênh SYSTEM, `templateId` và `templateData` không còn bắt buộc. Khi bật kênh phải có mã mẫu OTP hoặc mã mẫu thanh toán. `templateId` và `templateData` phải đi cùng nhau. Planner vốn đã bỏ qua tin thanh toán Zalo khi thiếu `templateData`.
- **Kênh của doanh nghiệp (CUSTOMER):** không đổi, không có trường OTP.
- **Mã mẫu chấp nhận chữ và số** (`^[0-9A-Za-z]{1,40}$`), vì ví dụ trong tài liệu Zalo có mã dạng hex.
- **Nút "Gửi thử OTP" trong Admin:** gửi mẫu OTP với mã ngẫu nhiên 6 số qua kênh hệ thống. Dùng chung giới hạn 5 lần thử mỗi phút và ghi kết quả thử lên kênh.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TAPortal.Integrations/Notifications/Notifications.cs`: thêm `Fields(channel, scope)` cho kênh Zalo SYSTEM (`otpTemplateId`, mẫu thanh toán không bắt buộc), `Validate(..., scope)` và `TemplateId()`.
- MODIFY `src/TAPortal.Integrations/Notifications/NotificationService.cs`: thêm `SystemOtpTemplateIdAsync`, `TestOtpAsync` và tách `TryTest`. Fields/Validate giờ nhận theo scope.
- MODIFY `src/TAPortal.Integrations/ZaloMiniApp/PhoneVerification.cs`: thêm `IOtpTemplateSource`, `ConfiguredOtpTemplate`, `SystemOtpTemplate` và đăng ký DI.
- MODIFY `src/TAPortal.Integrations/ZaloMiniApp/ZaloAccountService.cs`: panel và cổng xác minh dùng `IOtpTemplateSource`.
- MODIFY `src/TAPortal.Integrations/ZaloMiniApp/ZaloMiniApp.cs`: sửa chú thích của `OtpTemplateId` (nay là giá trị dự phòng).
- MODIFY `src/TAPortal.Web/Controllers/NotificationSettingsController.cs`: Save dùng Fields theo scope; Test nhận `kind=otp`.
- MODIFY `src/TAPortal.Web/Views/NotificationSettings/Index.cshtml`: thêm nút "Gửi thử OTP".
- MODIFY `src/TAPortal.Web/Views/Account/Zalo.cshtml`: câu báo chưa cấu hình mẫu OTP nay chỉ tới Admin.
- ADD `tests/TACustomerPortal.Tests/SafeZaloOtpTemplateTests.cs`: 7 test.
- MODIFY `tests/TACustomerPortal.Tests/SafeZaloMiniAppTests.cs`: sửa rig theo constructor mới.
- MODIFY `tests/TACustomerPortal.Tests/SafeZaloMiniAppPageTests.cs`: thêm fake `IOtpTemplateSource`.

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path: không có. Trường mới nằm trong JSON settings sẵn có của `dbo.NotificationChannels` (migration 020).
- Thứ tự chạy: NOT_APPLICABLE
- Preflight / Verify / Rollback: NOT_APPLICABLE
- Execution: NOT_APPLICABLE

## Test và kết quả
- `dotnet restore TAPortal.sln`: exit 0.
- `dotnet build TAPortal.sln -c Release --no-restore`: Build succeeded, 0 lỗi.
- `dotnet test --filter FullyQualifiedName~TACustomerPortal.Tests.Safe`: 820/820 pass, trong đó có 7 test mới `SafeZaloOtpTemplateTests`.
- `python -m unittest discover -s .codec/tools -p test_codec.py`: OK.
- `git diff --check`: sạch.
- Chưa xem trực tiếp trang Admin trên trình duyệt (NOT_RUN), vì trang cần SQL. Việc hiển thị các trường dựa trên `Fields(channel, scope)` và đã được test.

## Publish
- Project: TAPortal.Web, TACustomerPortal.Web, TAPortal.Webhooks (thay đổi nằm trong TAPortal.Integrations).
- Destination (chỉ từ config): `src\publish\deployed-<PROJECT_NAME>` qua `Deploy-TARemote.ps1`.
- Trạng thái: NOT_PUBLISHED. Chỉ deploy khi operator đồng ý.
- Artifact SHA256: chưa có.
- Smoke test: chưa chạy.

## Git
- Branch: `feature/zalo-mini-app` (đã merge origin/main 38b33c9 trước khi sửa).
- Commit SHA: ghi trong hand-off.
- PR: không có.
- Merge: PENDING (merge vào main sau khi push, theo OPERATOR STANDING AUTHORIZATION).
- Merge SHA: ghi trong hand-off.

## Rủi ro, quyết định và việc còn lại
- **Production ngày 2026-11-17 (đọc chỉ-đọc):** chưa có kênh SYSTEM/ZALO_OA. Kênh SMTP SYSTEM đang tắt. 0 OTP đã gửi.
- **Việc operator cần làm** (mình không đăng nhập hộ được):
  1. Trên ZBS Account (account.zalo.solutions), tạo "Template xác thực" theo nội dung đã duyệt và chờ Zalo duyệt.
  2. Kiểm tra OA đã xác thực và có gói, App đã được OA cấp quyền "Gửi tin qua số điện thoại", ZBS Account có số dư và đã liên kết với App.
  3. Sau khi deploy: vào Admin → Cài đặt thông báo → Zalo OA, nhập App ID, Secret key, Refresh token và Mã mẫu ZNS OTP, bật kênh, rồi bấm "Gửi thử OTP".
  4. Khi gửi thử thành công, nếu muốn bắt buộc xác minh SĐT thì bật `ZaloMiniApp:RequirePhoneVerification`. Đây là quyết định riêng của operator.
- **Rủi ro:**
  - Cổng xác minh SĐT tự bật khi `RequirePhoneVerification=true` và có mã mẫu. Nay mã mẫu có thể đến từ Admin, nhưng `RequirePhoneVerification` vẫn mặc định false nên không ai bị chặn.
  - Lỗi của Zalo (-118 số không có Zalo, -139/-141 người dùng từ chối nhận, -144/-147 hết hạn mức…) hiện hiện nguyên văn sau câu "Không gửi được mã qua Zalo". Chưa có câu thân thiện hơn; để lại làm sau nếu cần.
- **Rollback:** deploy `-Rollback`. Không có thay đổi DB.
