# Feature: ZALO-MINI-APP

- Feature ID: `ZALO-MINI-APP`
- Tên: Zalo Mini App: đăng nhập bằng số điện thoại Zalo, QR một lần, OTP ZNS xác minh số
- Mục tiêu: Zalo Mini App của T.A: đăng nhập bằng SĐT Zalo trùng SĐT tài khoản TA Portal / Hóa đơn tự động; sau đó thông báo giao dịch ngân hàng, tra cứu giao dịch, xem/xuất hóa đơn.
- Phạm vi / ngoài phạm vi: quy tắc nghiệp vụ (operator 2026-11-13): QR một lần 5 phút từ web, hoặc tên đăng nhập + mật khẩu; luôn phải trùng SĐT; nhiều tài khoản thì chọn; SĐT xác minh bằng OTP ZNS.
- Kiến trúc / bảng liên quan: `TAPortal.Integrations/ZaloMiniApp`, API `TACustomerPortal.Web` `/api/zalo-mini-app/*`; dbo.UserVerifiedPhones, dbo.PhoneOtpChallenges, dbo.ZaloMiniAppQrTokens, dbo.ZaloMiniAppLinks (migration 038); dbo.Users (đọc).
- Các phụ thuộc: kênh Zalo OA hệ thống (gói OA Tăng trưởng), cấu hình `ZaloMiniApp:*` trên server, Mini App trên mini.zalo.me.
- Trạng thái: ACTIVE

## Change history

| Change ID | Ngày UTC | Nội dung | Commit SHA | Migration | Publish | Merge |
|---|---|---|---|---|---|---|
| `ZALO-MINI-APP-CHG-20261113T053124Z-A0CC3C` | 2026-11-13 | Zalo Mini App: đăng nhập bằng số điện thoại Zalo, QR một lần, OTP ZNS xác minh số | — | NOT_EXECUTED | NOT_PUBLISHED | PENDING_APPROVAL |
| `ZALO-MINI-APP-CHG-20261113T063523Z-6DBAB9` | 2026-11-13 | Zalo Mini App: trang Zalo trên TA Portal và Hóa đơn tự động (đổi SĐT, OTP, QR, hủy liên kết), cổng xác minh SĐT | — | NOT_EXECUTED | NOT_PUBLISHED | PENDING_APPROVAL |
