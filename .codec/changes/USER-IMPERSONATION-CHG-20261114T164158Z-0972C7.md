# Change USER-IMPERSONATION-CHG-20261114T164158Z-0972C7

- Feature ID: `USER-IMPERSONATION`
- Change ID: `USER-IMPERSONATION-CHG-20261114T164158Z-0972C7`
- Created UTC: 2026-11-14T16:41:58.099176+00:00
- Tiêu đề: Trang Nguoi dung: menu 3 cham bi che va dong Razor lo ra o chan trang
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator (2026-11-14, ảnh chụp "ĐANG LỖI"): trên Quản lý người dùng, bấm ⋮ thì menu bị các dòng dưới che (ô cột Thao tác được ghim, mỗi ô là một lớp riêng); chân trang in nguyên đoạn code `@if (AdminPermissions.Has(...)) {` vì Razor đọc `hàng@if` thành địa chỉ email.

Đã sửa: ô Thao tác đang mở menu được nâng lên trên (`:has(.dropdown-menu.show)` + lớp `ta-menu-open` do JS gắn khi trình duyệt không hỗ trợ `:has`); tách `@if` ra khỏi chữ ở chân trang.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TAPortal.Web/wwwroot/css/ta-design.css`, `src/TAPortal.Web/wwwroot/js/ta-portal.js`, `src/TAPortal.Web/Views/Users/Index.cshtml`

## Migration SQL
- Không có.

## Test và kết quả
- Release build 0 lỗi; Safe tests 771/771.

## Publish
- portal. Xem report.

## Git
- Branch: feature/tx-sync
- Commit SHA: xem report
- Merge: main

## Rủi ro, quyết định và việc còn lại
- Chưa bấm thử menu trên portal thật sau khi đăng nhập.
