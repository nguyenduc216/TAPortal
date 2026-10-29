# Change TAPORTAL-SYSTEM-AUDIT-CHG-20261029T164002Z-80D551

- Feature ID: `TAPORTAL-SYSTEM-AUDIT`
- Change ID: `TAPORTAL-SYSTEM-AUDIT-CHG-20261029T164002Z-80D551`
- Created UTC: 2026-10-29T16:40:02.125798+00:00
- Tiêu đề: Admin form UX: modal mask fix, role names, searchable dropdowns, field icons, live password rules
- Trạng thái: IMPLEMENTED / BUILD_VERIFIED / LOCAL_PUBLISH_COMPLETE (Admin) / VISUAL_NOT_VERIFIED (production) / PRODUCTION_NOT_DEPLOYED
- Agent: Claude Code (claude-opus-5-5)

## Yêu cầu và phạm vi
Phản hồi operator (ảnh modal "Tạo tài khoản mới và cấp quyền"): modal bị lớp mask che; vai trò hiển thị mã (VIEWER); dropdown cần live search; text field cần icon bên trái; mật khẩu cần kiểm tra quy tắc realtime (đạt xanh, vi phạm đỏ). Chỉ Admin; không đổi nghiệp vụ, SQL, authorization.

## Nguyên nhân và cách sửa
- Mask: `.ta-page-enter` (animation fill-mode both) để lại `transform` trên main.ta-content → stacking context/containing block; modal Bootstrap nằm dưới backdrop. Sửa: ta-admin.css đặt `animation-fill-mode: backwards` cho `.ta-content.ta-page-enter`; ta-portal.js chuyển mọi modal ra `<body>`. (ta-design.css dùng chung giữ nguyên để không phải publish lại Customer — Customer không có modal.)
- Vai trò: PortalRoleText (Chỉ xem / Quản trị doanh nghiệp / Chủ sở hữu) cho modal, bảng và trang tổng hợp cũ; giá trị gửi vẫn OWNER/ADMIN/VIEWER.
- Dropdown: ta-portal.js nâng cấp mọi select.form-select thành combobox (tìm kiếm không dấu, bàn phím, optgroup, mục bị vô hiệu, popup fixed thoát vùng cuộn bảng/modal); select gốc vẫn submit, giữ onchange và required (thông báo hiển thị trên control).
- Text field: 27 ô nhập trong 6 view có icon trái; ô mật khẩu có nút hiện/ẩn.
- Mật khẩu: trống = trung tính, đạt = xanh ✓, vi phạm = đỏ ✕. Sửa lệch client/server: client trước dùng [A-Za-z] nên "đ" bị coi là ký tự đặc biệt; nay dùng \p{L}/\p{Nd} như PasswordPolicy (char.IsLetter/IsDigit).

## File thay đổi
- ADD src/TAPortal.Web/Services/PortalRoleText.cs
- MODIFY src/TAPortal.Web/wwwroot/js/ta-portal.js, wwwroot/css/ta-admin.css, Views/Shared/_Layout.cshtml (template icon)
- MODIFY Views/Customers/{Edit,PortalAccounts}, Views/CustomerPortalAccounts/Index, Views/Users/{Index,Edit}, Views/Account/ChangePassword
- MODIFY tests/TACustomerPortal.Tests/SafeCustomerWorkspaceTests.cs (+2: nhãn vai trò, icon text field)
- Không đụng: src/TAPortal.Shared, src/TACustomerPortal.Web, database/*

## Migration SQL (KHÔNG tự chạy)
- Không có. Execution: NOT_EXECUTED (không áp dụng)

## Test và kết quả
- Release build 0/0; safe tests 252/252 PASS; Codec unittest OK, validate PASS; git diff --check OK
- Visual: ảnh headless có tương tác (mở modal, gõ mật khẩu, tìm kiếm dropdown, dropdown trong bảng) — modal nằm trên mask; VISUAL_NOT_VERIFIED trên trình duyệt/IIS thật

## Publish
- TAPortal.Web → D:\T.A\TAPortal\TAPortal\publish\deployed-TAPortal.Web: exit 0, 2026-10-29T16:40:24Z→16:40:27Z, 62 files, SHA256 50265e3053b2c6c997a3c7dc7976ba2a63e482dda1d1394e04602c0b402c081d
- Backup publish\rollback\TAPORTAL-SYSTEM-AUDIT-CHG-20261029T164002Z-80D551\TAPortal.Web (f609696d…, xác minh trùng 100%); Customer NOT_REQUIRED (b39bb31c… giữ nguyên)
- PUBLISHED (local) / NOT_DEPLOYED; IIS NOT_CHECKED

## Git
- Source SHA 64c797bdd13a5aacf5395cbf4cab6453cabe47a2; delivery SHA báo sau push; PR #7 draft, merge PENDING_APPROVAL

## Rủi ro, quyết định và việc còn lại
- ta-design.css dùng chung vẫn có fill-mode both (Customer chưa có modal nên chưa ảnh hưởng); nên đồng bộ khi Customer có thay đổi tiếp theo.
- Customer Portal vẫn hiển thị mã vai trò (OWNER…) ở trang khách hàng — đổi khi operator đồng ý publish Customer.
- Rollback: chép thư mục backup; git revert 64c797bdd13a5aacf5395cbf4cab6453cabe47a2.
