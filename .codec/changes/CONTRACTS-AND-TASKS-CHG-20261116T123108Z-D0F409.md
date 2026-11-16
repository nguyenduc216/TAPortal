# Change CONTRACTS-AND-TASKS-CHG-20261116T123108Z-D0F409

- Feature ID: `CONTRACTS-AND-TASKS`
- Change ID: `CONTRACTS-AND-TASKS-CHG-20261116T123108Z-D0F409`
- Created UTC: 2026-11-16T12:31:08.061291+00:00
- Tiêu đề: Admin portal installable as an app (PWA) and web push notifications on desktop and mobile
- Trạng thái: PLANNED

## Yêu cầu và phạm vi
Operator 2026-11-16: "Thêm phần cài đặt app để tạo icon của T.A portal trên điện thoại. Và kiểm tra với trình duyệt chrome trên điện thoại có thể bật các thông báo được không để làm các thông báo đẩy từ web trên desktop và mobile".
- PWA: `/manifest.webmanifest` (public), `wwwroot/sw.js` (no caching), icons 192/512/maskable + monochrome badge from `ta-logo.png`; "Cài ứng dụng" button in the top bar when the browser offers it; iPhone/iPad and manual steps on Hồ sơ cá nhân.
- Web push: VAPID (RFC 8292) + aes128gcm (RFC 8291) with .NET built-ins only (no new package). VAPID private key DPAPI-protected in `dbo.WebPushKeys`; devices in `dbo.WebPushSubscriptions` (max 20 per user; endpoints limited to the browsers' push services; 404/410 removes the device). Hồ sơ cá nhân > Thông báo đẩy: bật / tắt / gửi thử per device. Blocked during "Truy cập hộ"; sign-out removes the device.
- Not in scope: which business events send a push (to be decided by the operator).

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `database/migrations/059-web-push.sql`, `database/scripts/105-verify-web-push.sql`, `database/scripts/106-rollback-web-push.sql`
- ADD `src/TAPortal.Web/Services/WebPush.cs`, `src/TAPortal.Web/Controllers/PushController.cs`, `src/TAPortal.Web/wwwroot/sw.js`, `src/TAPortal.Web/wwwroot/js/ta-app.js`, `src/TAPortal.Web/wwwroot/images/app-icon-192.png`, `app-icon-512.png`, `app-icon-maskable-512.png`, `notify-badge.png`
- MODIFY `src/TAPortal.Web/Program.cs`, `Controllers/AccountController.cs`, `Views/Shared/_Layout.cshtml`, `Views/Account/Profile.cshtml`, `wwwroot/css/ta-admin.css`, `src/TAPortal.Shared/UiIcon.cs` (icons download, device-mobile, bell-off)
- ADD/MODIFY tests: `SafeWebPushTests.cs`, `SafeAdminWorkTests.cs`, `SafeReleaseRegressionTests.cs` (manifest reviewed anonymous), `SafeAdminPermissionTests.cs` (PushController personal like AccountController)

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path: `database/migrations/059-web-push.sql`
- Thứ tự chạy: after 058
- Preflight / Verify / Rollback: `database/scripts/105-verify-web-push.sql` / `database/scripts/106-rollback-web-push.sql`
- Execution: NOT_EXECUTED

## Test và kết quả
- Release build 0 errors; Safe* tests 791 (2 inventory tests updated for the new endpoints) — see final run below.
- Encryption verified by decrypting as the browser (independent HMAC implementation of RFC 8291); VAPID ES256 signature verified.

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

## Migration execution
- 059-web-push.sql: PRECHECK NOT_APPLIED; DRYRUN PASS/NOT_APPLIED (trancount 0); gates PASS (791 Safe tests); REAL RUN VERIFY_STATUS=PASS => EXECUTED 2026-11-16T12:47:28Z

