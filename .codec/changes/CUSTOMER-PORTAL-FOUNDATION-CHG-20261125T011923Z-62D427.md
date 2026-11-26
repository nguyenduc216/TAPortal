# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261125T011923Z-62D427

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261125T011923Z-62D427`
- Created UTC: 2026-11-25T01:19:23.384800+00:00
- Tiêu đề: Hướng dẫn kết nối Zalo OA cạnh màn hình Cài đặt thông báo
- Trạng thái: DEPLOYED

## Yêu cầu và phạm vi
Operator (2026-11-25): mỗi doanh nghiệp tự dùng Zalo OA của mình để gửi ZNS cho khách (T.A cũng cấu hình OA của T.A trong tài khoản doanh nghiệp của T.A); cần trang hướng dẫn trong app, đặt cạnh màn hình Cài đặt thông báo. Web công khai làm sau.
Đã làm: `GET /Notifications/ZaloGuide` (OWNER/ADMIN như trang cài đặt) — 7 bước: xác thực OA, tạo ứng dụng Zalo (App ID, Secret key), lấy refresh token, kích hoạt ZNS/ZBS + nạp ví, đăng ký mẫu "Xác nhận thanh toán" (loại Giao dịch, quy tắc duyệt mẫu của Zalo 10/2026), điền vào app (bảng ô nhập, JSON mẫu, bảng mã {buyer} {company} {order_no} {amount} {paid} {total} {status} {time}), gửi thử và bật kênh; bảng lỗi thường gặp theo đúng thông báo của `NotificationTransport`. Thẻ Zalo OA ở `/Notifications` có ghi chú dẫn tới trang. Không đổi logic gửi tin, không đổi DB.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `src/TACustomerPortal.Web/Views/Notifications/ZaloGuide.cshtml`
- MODIFY `src/TACustomerPortal.Web/Controllers/NotificationsController.cs` (action ZaloGuide)
- MODIFY `src/TACustomerPortal.Web/Views/Notifications/Index.cshtml` (ghi chú + link trong thẻ Zalo OA)
- MODIFY `src/TACustomerPortal.Web/wwwroot/css/customer.css` (`.cp-guide*`)
- MODIFY `tests/TACustomerPortal.Tests/SafeCustomerPaymentsTests.cs` (test mới + trang vào kiểm tra partner neutrality)

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path:
- Thứ tự chạy:
- Preflight / Verify / Rollback:
- Execution: NOT_APPLICABLE (không có migration)

## Test và kết quả
- `dotnet restore` OK; Release build 0 lỗi; Safe tests 626/626 (thêm `TheZaloGuideIsLinkedFromTheZaloCardAndUsesOnlyRealPlaceholders`: MEMBER không vào được, link trong thẻ Zalo, JSON mẫu hợp lệ với `NotificationCatalog.TemplateData`, mọi mã {..} trong trang thuộc `NotificationPlanner.Values`); `/notifications/zaloguide` thêm vào `PaymentPagesNeverNamePartners`.
- Xem HTML render từ test host (tĩnh): 1440 px và 375 px không tràn ngang, 7 bước, khối JSON xuống dòng.

## Publish
- Project: TACustomerPortal.Web (target `invoice`), qua `deploy/windows/Deploy-TARemote.ps1`
- Destination (chỉ từ config): `src\publish\deployed-TACustomerPortal.Web` → server `TA.Invoice`
- Trạng thái: DEPLOYED 2026-11-26T05:31:30Z (server backup `C:\TADeployackups\invoice-20261126-123127`, giữ 1 bản)
- Source commit: c126e55; 74 files
- Artifact SHA256: 67f4a3865cf6193e50f2dd67453b58f2cc217d26d0cd5c50b93f483ec2dee891
- Package SHA256: 9EAA808C7929F0CE823301235342C4DFEF32A9BD9FA4F371879671E56697888F
- Audit: PASSED (Release build 0 lỗi, Safe 830/830 trên nền main)
- Smoke test: health HTTP 200; `/Notifications/ZaloGuide` → 302 về đăng nhập (đúng, cần đăng nhập); DLL trên server chứa view hướng dẫn và link, CSS có `.cp-guide*`.
- Lần chạy đầu từ feature/customer-portal-mvp01 bị audit chặn (khoảng 57bdaea..HEAD có 24 migration của main) — không push/deploy gì; đã chuyển sang nhánh từ main.

## Git
- Branch: feature/zalo-oa-guide (tạo từ origin/main 3ff2977, vì production invoice chạy main 57bdaea; feature/customer-portal-mvp01 đang chậm hơn main nên không deploy từ đó)
- Commit SHA: c126e55
- PR:
- Merge: main (fast-forward, theo OPERATOR STANDING AUTHORIZATION)
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Tên nút/trang của Zalo có thể đổi; trang đã ghi chú điều này. Quy định mẫu tin tham chiếu zalo.solutions (cập nhật 10/2026).
- Kênh Zalo chỉ gửi sự kiện PAYMENT tới SĐT người mua trên đơn (đúng code hiện tại); ghi rõ trong trang.
- Hướng dẫn trên website công khai: chưa làm (operator: làm sau).
