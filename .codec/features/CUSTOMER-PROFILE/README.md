# Feature: CUSTOMER-PROFILE

- Feature ID: `CUSTOMER-PROFILE`
- Tên: Hồ sơ khách hàng — thay file Excel nội bộ "Thông tin khách hàng.xlsx" (mỗi khách hàng một sheet)
- Mục tiêu: lưu trên TAPortal (Admin) toàn bộ thông tin khách hàng và thông tin đăng nhập các hệ thống của khách hàng (thuế điện tử, hóa đơn, chữ ký số, BHXH, ngân hàng, phần mềm…), mật khẩu được mã hóa, có phân quyền và nhật ký; sau đó là dịch vụ/phí, nhân viên phụ trách, theo dõi tờ khai theo kỳ, vấn đề/cơ hội.
- Phạm vi / ngoài phạm vi: Trong phạm vi: Admin (`TAPortal.Web`) Customer Workspace. Ngoài phạm vi: Customer Portal không đọc các bảng này; không import dữ liệu Excel bằng SQL/seed (giai đoạn 2 làm chức năng Import có màn hình duyệt).
- Kiến trúc / bảng liên quan: thiết kế ở `docs/customer-profile-design.md`. Giai đoạn 1: `dbo.CustomerProfiles` (1:1 `dbo.Customers`), `dbo.CustomerRepresentatives`, `dbo.CustomerSystemAccounts` (migration 013). Mật khẩu/PIN mã hóa AES-256-GCM bằng một khóa cố định `CustomerSecrets__Key` (associated data `TAPortal.CustomerSecrets.v1`) trong `TAPortal.Web`; DB chỉ chứa `varbinary`.
- Các phụ thuộc: `CustomerSecrets__Key` cho TAPortal.Web trên server (PENDING_OPERATOR, sinh bằng `deploy/windows/New-TACustomerSecretKey.ps1`) — chưa có hoặc sai định dạng thì không lưu được mật khẩu; `dbo.AuditLogs` ghi được — chưa có thì không hiện được mật khẩu.
- Trạng thái: ACTIVE

## Quyết định (2026-11-03)
1. Hồ sơ doanh nghiệp là bảng 1:1 `dbo.CustomerProfiles` thay vì thêm cột vào `dbo.Customers`: các màn hình khách hàng hiện có không phụ thuộc migration 013, rollback chỉ là drop bảng mới.
2. Một bảng chung `dbo.CustomerSystemAccounts` cho mọi loại tài khoản (12 `SystemType`), không giới hạn số tài khoản mỗi loại; chứng thư số dùng cùng bảng (`CertificateType`, `ExpiresAt`).
3. Mật khẩu/PIN chỉ ghi (write-only) trên form; chỉ xem qua `RevealSecret` (quyền `CRM.CUSTOMERS.SECRETS.REVEAL`, SYS_ADMIN có sẵn), mỗi lần xem ghi `CUSTOMER_SECRET_REVEALED` vào `dbo.AuditLogs` bắt buộc (không có audit thì không trả mật khẩu). Audit không chứa mật khẩu, CCCD, ngày sinh.
4. Từ chối lưu mật khẩu khi chưa cấu hình `CustomerSecrets:Key` hợp lệ (base64 của 32 byte).
5. Xóa tài khoản hệ thống là xóa mềm và xóa luôn ciphertext.
6. (2026-11-04) Operator chọn một khóa AES-256 cố định thay cho Data Protection: Data Protection tự sinh key mới ~90 ngày/lần nên phải backup thư mục key ring định kỳ, quá bất tiện. Khóa cố định không tự đổi; backup duy nhất là một bản sao chuỗi khóa do operator giữ ngoài server (trình quản lý mật khẩu/bản giấy). Không dùng khóa khôi phục RSA (key escrow). Không đổi khóa khi đã có mật khẩu lưu (dữ liệu cũ sẽ không đọc được); nếu cần xoay khóa thì làm thành change riêng (byte version trong ciphertext đã dành sẵn).
7. `DataProtection__KeyPath` của Admin vẫn chỉ phục vụ cookie đăng nhập/antiforgery, không liên quan mật khẩu khách hàng.
8. (2026-11-05, giai đoạn 2) Dữ liệu Excel vào hệ thống qua chức năng Import trong Admin (không seed/SQL): đọc .xlsx bằng reader nội bộ (không thêm package), khớp khách hàng theo MST, chỉ điền trường còn trống, không tạo trùng người đại diện/tài khoản (khóa: họ tên; loại + đăng nhập + nhà cung cấp/phần mềm + số TK + link), bổ sung mật khẩu còn thiếu khi nhập lại. File chỉ giữ trong bộ nhớ 30 phút, gắn với người upload, dùng một lần. Ghi chú nghi là mật khẩu (nằm sai cột) không được nhập, chỉ cảnh báo.
9. MST 12 số (số định danh cá nhân dùng làm MST cho cá nhân/HKD) được chấp nhận trong Admin như Customer Portal đã làm (`TaxCodePolicy`).

## Change history

| Change ID | Ngày UTC | Nội dung | Commit SHA | Migration | Publish | Merge |
|---|---|---|---|---|---|---|
| `CUSTOMER-PROFILE-CHG-20261103T183438Z-C56EAB` | 2026-11-03 | Customer profile phase 1: general info, representatives, encrypted system accounts | `769f167` | EXECUTED (013) | DEPLOYED portal (`7492d78`) | main |
| `CUSTOMER-PROFILE-CHG-20261105T152008Z-203021` | 2026-11-05 | Customer profile phase 2: import the customer workbook (Excel) with a review screen | `4a89199` | NOT_APPLICABLE | DEPLOYED portal (`4a89199`) | main |

## Current status (2026-11-05)
Giai đoạn 1 (C56EAB) DEPLOYED portal, migration 013 EXECUTED, đã merge main. Giai đoạn 2 (203021): chức năng "Nhập từ Excel" trong Khách hàng (đọc file, màn hình duyệt, lưu theo lựa chọn). Việc còn lại của operator: sinh và cấu hình `CustomerSecrets__Key` (giữ một bản sao ngoài server); nếu nhập trước khi có khóa thì nhập lại cùng file sau đó để bổ sung mật khẩu.

2026-11-06 (17A9E4): "Hiện"/"Sao chép" mật khẩu báo không ghi được audit vì `dbo.AuditLogs` production là bảng cũ (ActionCode/EntityType), thiếu cột `Action`/`EntityName` mà mọi audit của Admin ghi vào. Migration 025 thêm 2 cột (nullable, không đổi dữ liệu cũ): EXECUTED. Không đổi code, không deploy.
