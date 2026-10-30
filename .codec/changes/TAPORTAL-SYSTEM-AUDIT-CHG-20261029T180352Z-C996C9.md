# Change TAPORTAL-SYSTEM-AUDIT-CHG-20261029T180352Z-C996C9

- Feature ID: `TAPORTAL-SYSTEM-AUDIT`
- Change ID: `TAPORTAL-SYSTEM-AUDIT-CHG-20261029T180352Z-C996C9`
- Created UTC: 2026-10-29T18:03:52Z
- Tiêu đề: Customer Portal redesign v2: gold sidebar, blue primary, compact SaaS layout
- Trạng thái: IMPLEMENTED / BUILD_VERIFIED / LOCAL_PUBLISH_COMPLETE (Customer) / VISUAL_NOT_VERIFIED (production) / PRODUCTION_NOT_DEPLOYED
- Agent: Claude Code (claude-opus-5-5)

## Yêu cầu
Operator không thích giao diện top-nav xanh lá: muốn sidebar menu trái màu vàng đậm, ít xanh lá, màu chính xanh dương, chữ nhỏ để lưới rộng hơn, hiện đại, mượt, chuẩn SaaS.

## Thiết kế
- Sidebar trái 248px, nền gradient vàng đậm #A16207→#7A4A00 (chữ trắng 4.92–7.48:1); nhóm menu "Không gian làm việc" / "Tài khoản"; thẻ doanh nghiệp đang làm việc (bấm để đổi); khung hỗ trợ + đăng xuất ở chân. Mục đang mở: nền trắng, chữ xanh dương.
- Topbar trắng mờ: nút thu gọn/mở menu, breadcrumb (mã KH › trang), chip "Đổi doanh nghiệp" (xanh dương), menu tài khoản.
- Màu chính xanh dương #2563EB (trắng 5.17:1); xanh lá chỉ cho trạng thái hoạt động/đạt; cỡ chữ nền 13px; lưới auto-fit nên nội dung dùng hết chiều rộng; banner doanh nghiệp xanh dương gọn.
- ≥1200px: sidebar cố định, thu gọn thành rail icon (nhớ lựa chọn); <1200px: drawer có overlay, nút đóng, Esc (ta-shell.js dùng chung).
- Đăng nhập: panel vàng đậm + thẻ form xanh dương.
- CurrentCustomerContext cache theo request: sidebar không thêm truy vấn ở Tổng quan/Hồ sơ doanh nghiệp; lỗi tra cứu không làm hỏng trang lỗi.

## File
- src/TACustomerPortal.Web/Views/Shared/_Layout.cshtml, wwwroot/css/customer.css, Security/CustomerSecurity.cs
- tests: SafeUiDesignSystemTests (kiểm tra thiết kế mới), VisualSnapshotTests (store mẫu cho ảnh)

## Kiểm thử
- Release build 0/0; safe tests 260/260; Codec PASS; E2E (Chrome, HTTP cục bộ): chuyển trang không tải lại, menu active, breadcrumb, tiêu đề, thẻ doanh nghiệp đúng
- Ảnh: dashboard, hồ sơ doanh nghiệp, đăng nhập, đổi mật khẩu, chọn doanh nghiệp (1440/1024/390), drawer mobile, rail thu gọn

## Publish
- TACustomerPortal.Web: exit 0, 2026-10-29T18:10:47Z→18:10:51Z, 55 files, SHA256 45728db69dd6302831edf5f8613fc5e45b57dec069a0e46b6095c39449dc0ddc; không secret
- Backup publish\rollback\TAPORTAL-SYSTEM-AUDIT-CHG-20261029T180352Z-C996C9\TACustomerPortal.Web (d70a3736…); Admin NOT_REQUIRED (cc224fd3…)
- NOT_DEPLOYED; IIS NOT_CHECKED; không SQL

## Git
- Source SHA c06f198c2a4987e305956b70e250f95f63d5d117; base 6501562ae534ca2cc27fac289feea5b0e080d82c; delivery SHA báo sau push; PR #7 chưa merge
