# TAPortal.ZaloMiniApp — Mini App T.A trên Zalo

Ứng dụng chạy trong Zalo (React + zmp-ui + zmp-sdk, Vite). Gọi API `/api/zalo-mini-app/*` của Hóa đơn tự động
(`src/TACustomerPortal.Web/Controllers/ZaloMiniAppController.cs`). Feature Codec: `ZALO-MINI-APP`.

## Quy tắc đăng nhập (operator 2026-11-13)
- Chỉ số điện thoại: SĐT Zalo (Zalo xác thực, server tự đọc) phải trùng SĐT đăng ký của tài khoản TA Portal / Hóa đơn tự động.
- Cách 1: quét mã QR một lần (5 phút) trên trang Zalo của web → app mở với `?qr=…`.
- Cách 2: tên đăng nhập + mật khẩu, vẫn phải trùng SĐT.
- Zalo liên kết nhiều tài khoản → chọn tài khoản. Đăng xuất = hủy liên kết.
- App không bao giờ gửi số điện thoại lên server, chỉ gửi token của Zalo.

## Chạy
```bash
npm install
npm run mock      # mọi màn hình trong trình duyệt thường, Zalo và API giả (http://localhost:3000)
npm run dev       # API thật qua proxy của Vite; chỉ có Zalo thật khi mở trong Zalo
npm run build     # tsc + vite build vào dist/
```
Kịch bản giả: `?mock=accounts` (chọn tài khoản), `?mock=linked` (đã liên kết), `?mock=error` (SĐT không trùng),
`?qr=abc` (mở từ QR). Mật khẩu giả đúng: `123`.

## Đưa lên Zalo (cần Mini App ID)
1. `npm install -g zmp-cli`, `zmp login`.
2. Ghi Mini App ID vào `zmp-cli.json` (`{"app_id":"<id>"}`), rồi `zmp deploy` (bản Development / Testing để quét thử).
3. Trong mini.zalo.me: khai báo domain `hoadon.tuvangiaiphapta.vn`; xin quyền số điện thoại.
4. Server: `ZaloMiniApp:AppId`, `ZaloMiniApp:SecretKey` (chỉ nhập trên server, không commit).
